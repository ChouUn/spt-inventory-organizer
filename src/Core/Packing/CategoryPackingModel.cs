using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Google.OrTools.Sat;

namespace ChouUn.StashMaster.Core.Packing;

/// <summary>各网格内部加权空间与类别跨度，再累加分数共同求解。</summary>
internal static class CategoryPackingModel
{
    public static ContainerPackResult? Solve(IReadOnlyList<PackRequest> requests,
        ContainerPackResult baseline, double seconds, out string status)
    {
        var elapsed = Stopwatch.StartNew();
        var model = new CpModel();
        var grids = new List<List<CpSatModel.Rectangle>>();
        var objectives = new List<IReadOnlyList<PackingObjective>>();
        var details = new List<string>();
        var layoutHints = new ContainerPackResult(requests.Select((r, i) =>
            CategoryOrder.Penalty(r, baseline.Grids[i]) == 0
                ? baseline.Grids[i] : CategoryOrder.Hint(r, baseline.Grids[i]))
            .ToArray());
        for (int grid = 0; grid < requests.Count; grid++)
        {
            PackRequest request = requests[grid];
            PackResult before = baseline.Grids[grid];
            var spans = Enumerable.Range(0, CategoryPacking.Depth(request))
                .Select(_ => new List<IntVar>()).ToArray();
            NoOverlap2dConstraint overlap = model.AddNoOverlap2D();
            foreach (FixedBlock block in request.Fixed)
            {
                overlap.AddRectangle(model.NewFixedSizeIntervalVar(
                    LinearExpr.Constant(block.X), block.Width, "fixed-x"),
                    model.NewFixedSizeIntervalVar(
                        LinearExpr.Constant(block.Y), block.Height, "fixed-y"));
            }
            IntVar top = model.NewIntVar(0, request.Height, "top-" + grid);
            var rectangles = new List<CpSatModel.Rectangle>();
            grids.Add(rectangles);
            PackResult layoutHint = layoutHints.Grids[grid];
            var hints = layoutHint.Placements.ToDictionary(p => p.Id);
            foreach (PackItem item in request.Items)
            {
                CpSatModel.Rectangle rect =
                    CpSatModel.AddItem(model, overlap, request, item, top);
                rectangles.Add(rect);
                bool present = hints.TryGetValue(item.Id, out Placement? p);
                model.AddHint(rect.Present, present);
                model.AddHint(rect.X, present ? p!.X : 0);
                model.AddHint(rect.Y, present ? p!.Y : 0);
                model.AddHint(rect.Rotated, present && p!.Rotated);
                model.AddHint(rect.EndX, present
                    ? p!.X + (p.Rotated ? item.Height : item.Width) : 0);
                model.AddHint(rect.EndY, present
                    ? p!.Y + (p.Rotated ? item.Width : item.Height) : 0);
            }
            LinearExpr area = LinearExpr.Sum(rectangles.Select(r =>
                r.Present * (r.Item.Width * r.Item.Height)));
            IntVar available = model.NewIntVar(0,
                request.Width * request.Height, "available");
            long[] cells = CpSatPacker.AvailableCells(request);
            model.AddElement(top, cells, available);
            model.Add(area <= available);
            int height = CpSatPacker.Height(request, layoutHint);
            model.AddHint(top, height);
            model.AddHint(available, cells[height]);
            var categoryVariables = new Dictionary<string, IntVar>();
            foreach (var group in rectangles.SelectMany(r => r.Item.CategoryPath
                .Select((id, level) => (Id: id, Level: level, Rect: r)))
                .GroupBy(entry => (entry.Id, entry.Level)))
            {
                string members = string.Join(",", group.Select(r => r.Rect.Item.Id)
                    .OrderBy(id => id, StringComparer.Ordinal));
                if (categoryVariables.TryGetValue(members, out IntVar? existing))
                {
                    spans[group.Key.Level].Add(existing);
                    continue;
                }
                // inclusive bottom - first row；缺席类别强制为零。
                IntVar first = model.NewIntVar(0, request.Height - 1, "category-first");
                IntVar last = model.NewIntVar(0, request.Height - 1, "category-last");
                IntVar span = model.NewIntVar(0, request.Height - 1, "category-span");
                BoolVar active = model.NewBoolVar("category-present");
                model.AddMaxEquality(active, group.Select(r => r.Rect.Present));
                model.Add(span == last - first);
                model.Add(first == 0).OnlyEnforceIf(active.Not());
                model.Add(last == 0).OnlyEnforceIf(active.Not());
                foreach (CpSatModel.Rectangle rect in group.Select(r => r.Rect))
                {
                    model.Add(first <= rect.Y).OnlyEnforceIf(rect.Present);
                    model.Add(last >= rect.EndY - 1).OnlyEnforceIf(rect.Present);
                }
                var placed = group.Select(r => r.Rect)
                    .Where(r => hints.ContainsKey(r.Item.Id)).ToArray();
                int min = placed.Select(r => hints[r.Item.Id].Y).DefaultIfEmpty().Min();
                int max = placed.Select(r =>
                {
                    Placement p = hints[r.Item.Id];
                    return p.Y + (p.Rotated ? r.Item.Width : r.Item.Height) - 1;
                }).DefaultIfEmpty().Max();
                model.AddHint(active, placed.Length > 0);
                model.AddHint(first, min);
                model.AddHint(last, max);
                model.AddHint(span, max - min);
                spans[group.Key.Level].Add(span);
                categoryVariables.Add(members, span);
            }
            objectives.Add(GridObjectives(model, request, before, top, area,
                spans, out string detail));
            details.Add($"grid={grid} {detail}");
        }
        foreach (var item in grids.SelectMany(g => g).GroupBy(r => r.Item.Id))
        {
            model.Add(LinearExpr.Sum(item.Select(r => r.Present)) <= 1);
        }

        PackingSymmetry.Add(model, requests, grids.SelectMany((g, index) =>
            g.Select(r => (index, r))), layoutHints);
        CategoryOrderModel.Add(model, requests, grids, layoutHints);
        bool orderedBaseline = requests.Select((r, i) =>
            CategoryOrder.Penalty(r, baseline.Grids[i]) == 0).All(x => x);
        IReadOnlyList<PackingObjective> blocks =
            PackingObjective.Sum(model, objectives);
        var diagnostics = new List<string>();
        if (blocks.Count == 0)
        {
            status = "Optimal; objective=grid-sum; " + string.Join("; ", details);
            return baseline;
        }
        using var solver = new CpSolver();
        using var progress = new PackingProgress(requests, baseline,
            callback => ReadResult(variable => callback.Value(variable)),
            solver.StopSearch);
        bool hasSolution = false;
        status = "Optimal";
        for (int stage = 0; stage < blocks.Count; stage++)
        {
            PackingObjective block = blocks[stage];
            double remaining = seconds - elapsed.Elapsed.TotalSeconds;
            if (remaining <= 0)
            {
                status = "Feasible; budget-exhausted before " + block.Label;
                break;
            }
            LinearExpr objective = block.Expression;
            long ceiling = hasSolution ? solver.Value(objective) : block.Current;
            if (hasSolution || orderedBaseline) { model.Add(objective <= ceiling); }
            model.Minimize(objective);
            var stageClock = Stopwatch.StartNew();
            solver.StringParameters = "max_time_in_seconds:"
                + remaining.ToString("R", CultureInfo.InvariantCulture)
                + ",num_workers:1,random_seed:0";
            CpSolverStatus solved;
            try
            {
                solved = solver.Solve(model, progress.Callback);
            }
            finally
            {
                progress.EndSearch();
            }
            diagnostics.Add($"block={block.Label}:{solved}:"
                + $"{stageClock.ElapsedMilliseconds}ms");
            status = solved.ToString() + "; objective=grid-sum; "
                + string.Join("; ", diagnostics) + "; " + progress.Diagnostic
                + "; " + string.Join("; ", details);
            if (solved != CpSolverStatus.Optimal && solved != CpSolverStatus.Feasible)
            {
                return hasSolution ? progress.Best : null;
            }
            hasSolution = true;
            // 下一阶段沿用刚求出的完整可行解，不重新猜测物品位置。
            model.ClearHints();
            model.Model.SolutionHint = new PartialVariableAssignment();
            // 上面的 Feasible/Optimal 状态保证存在求解响应。
            CpSolverResponse response = solver.Response!;
            for (int variable = 0; variable < response.Solution.Count; variable++)
            {
                model.Model.SolutionHint.Vars.Add(variable);
                model.Model.SolutionHint.Values.Add(response.Solution[variable]);
            }
            if (solved != CpSolverStatus.Optimal || progress.Stopped)
            {
                break;
            }
            model.Add(objective == solver.Value(objective));
        }
        return hasSolution ? progress.Best : null;

        ContainerPackResult ReadResult(Func<IntVar, long> value)
        {
            var results = new List<PackResult>();
            for (int grid = 0; grid < requests.Count; grid++)
            {
                Placement[] placements = grids[grid]
                    .Where(r => value(r.Present) != 0)
                    .Select(r => new Placement(r.Item.Id, (int)value(r.X),
                        (int)value(r.Y), value(r.Rotated) != 0))
                    .ToArray();
                var ids = new HashSet<string>(placements.Select(p => p.Id));
                results.Add(new PackResult(placements,
                    requests[grid].Items.Where(i => !ids.Contains(i.Id)).ToArray()));
            }
            return new ContainerPackResult(results);
        }
    }

    /// <summary>仅在本网格内确定优先级；固定项省去常数，倍率仍按原上界计算。</summary>
    private static IReadOnlyList<PackingObjective> GridObjectives(CpModel model,
        PackRequest request, PackResult before, IntVar top, LinearExpr area,
        IReadOnlyList<List<IntVar>> spans, out string detail)
    {
        int areaBefore = CpSatPacker.Area(request, before);
        int heightBefore = CpSatPacker.Height(request, before);
        int areaUpper = CategoryPacking.AreaUpperBound(request);
        bool allRequired = request.Items.All(i => i.Required);
        long heightWeight = request.Height + 1L;
        LinearExpr space = allRequired ? top
            : top + (areaUpper - area) * heightWeight;
        long spaceBefore = heightBefore + (allRequired ? 0
            : (areaUpper - areaBefore) * heightWeight);
        int lowerHeight = Array.FindIndex(CpSatPacker.AvailableCells(request),
            a => a >= areaBefore);
        bool spaceOptimal = areaBefore == areaUpper && heightBefore == lowerHeight
            && CategoryOrder.Penalty(request, before) == 0;
        if (spaceOptimal) { model.Add(space == spaceBefore); }
        var objectives = new List<PackingObjective>
        {
            new(space, request.Height + (allRequired ? 0 : areaUpper * heightWeight),
                spaceBefore, "space", spaceOptimal),
        };
        var signatures = new HashSet<string>();
        var notes = new List<string>();
        bool precedingFixed = spaceOptimal;
        for (int level = 0; level < spans.Count; level++)
        {
            string label = level.ToString(CultureInfo.InvariantCulture);
            string signature = string.Join(",", spans[level].Select(v => v.Index)
                .OrderBy(index => index));
            if (!signatures.Add(signature)) { notes.Add(label + "=shared"); }
            LinearExpr expression = LinearExpr.Sum(spans[level]);
            long current = CategoryPacking.Span(request, before, level);
            long upper = CategoryPacking.UpperBound(request, level);
            long lower = spaceOptimal
                ? CategoryPacking.LowerBound(request, areaBefore, level) : 0;
            model.Add(expression >= lower);
            bool fixedValue = upper == lower || (current == lower && precedingFixed);
            if (fixedValue)
            {
                model.Add(expression == current);
                notes.Add(label + "=bound");
            }
            objectives.Add(new PackingObjective(expression, upper, current,
                label, fixedValue));
            precedingFixed &= fixedValue;
        }
        detail = "layers=" + spans.Count
            + (notes.Count == 0 ? "" : ", " + string.Join(",", notes));
        return objectives;
    }
}
