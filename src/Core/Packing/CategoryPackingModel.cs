using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Google.OrTools.Sat;

namespace ChouUn.InventoryOrganizer.Core.Packing;

/// <summary>联合优化面积、高度和类别跨度；全部物品含单格参与位置求解。</summary>
internal static class CategoryPackingModel
{
    public static ContainerPackResult? Solve(IReadOnlyList<PackRequest> requests,
        ContainerPackResult baseline, double seconds, out string status)
    {
        var elapsed = Stopwatch.StartNew();
        var model = new CpModel();
        var grids = new List<List<CpSatModel.Rectangle>>();
        var tops = new List<IntVar>();
        int depth = requests.Max(CategoryPacking.Depth);
        var spans = Enumerable.Range(0, depth)
            .Select(_ => new List<IntVar>()).ToArray();
        var areas = new List<LinearExpr>();
        for (int grid = 0; grid < requests.Count; grid++)
        {
            PackRequest request = requests[grid];
            PackResult before = baseline.Grids[grid];
            NoOverlap2dConstraint overlap = model.AddNoOverlap2D();
            foreach (FixedBlock block in request.Fixed)
            {
                overlap.AddRectangle(model.NewFixedSizeIntervalVar(
                    LinearExpr.Constant(block.X), block.Width, "fixed-x"),
                    model.NewFixedSizeIntervalVar(
                        LinearExpr.Constant(block.Y), block.Height, "fixed-y"));
            }
            IntVar top = model.NewIntVar(0, request.Height, "top-" + grid);
            tops.Add(top);
            var rectangles = new List<CpSatModel.Rectangle>();
            grids.Add(rectangles);
            var hints = before.Placements.ToDictionary(p => p.Id);
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
            areas.Add(area);
            IntVar available = model.NewIntVar(0,
                request.Width * request.Height, "available");
            long[] cells = CpSatPacker.AvailableCells(request);
            model.AddElement(top, cells, available);
            model.Add(area <= available);
            int height = CpSatPacker.Height(request, before);
            model.AddHint(top, height);
            model.AddHint(available, cells[height]);
            foreach (var group in rectangles.SelectMany(r => r.Item.CategoryPath
                .Select((id, level) => (Id: id, Level: level, Rect: r)))
                .GroupBy(entry => (entry.Id, entry.Level)))
            {
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
            }
        }
        foreach (var item in grids.SelectMany(g => g).GroupBy(r => r.Item.Id))
        {
            model.Add(LinearExpr.Sum(item.Select(r => r.Present)) <= 1);
        }
        PackingSymmetry.Add(model, requests, grids.SelectMany((g, index) =>
            g.Select(r => (index, r))), baseline);
        // 分阶段锁定已证明的高层目标，避免多层大权重溢出及浮点目标精度损失。
        long heightWeight = requests.Sum(r => (long)r.Height) + 1;
        LinearExpr space = LinearExpr.Sum(tops) - LinearExpr.Sum(areas) * heightWeight;
        long spaceBefore = ContainerPacker.Height(requests, baseline)
            - ContainerPacker.Area(requests, baseline) * heightWeight;
        var objectives = new[] { space }
            .Concat(spans.Select(level => LinearExpr.Sum(level))).ToArray();
        long[] costs = new[] { spaceBefore }.Concat(Enumerable.Range(0, depth)
            .Select(d => (long)requests.Select((r, i) =>
                CategoryPacking.Span(r, baseline.Grids[i], d)).Sum())).ToArray();
        bool spaceOptimal = requests.Select((r, i) =>
        {
            int area = CpSatPacker.Area(r, baseline.Grids[i]);
            return area == CategoryPacking.AreaUpperBound(r)
                && CpSatPacker.Height(r, baseline.Grids[i])
                    == Array.FindIndex(CpSatPacker.AvailableCells(r), a => a >= area);
        }).All(optimal => optimal);
        using var solver = new CpSolver();
        bool hasSolution = false;
        status = "Optimal";
        for (int stage = 0; stage < objectives.Length; stage++)
        {
            if (stage == 0 && spaceOptimal)
            {
                model.Add(space == spaceBefore);
                continue;
            }
            double remaining = seconds - elapsed.Elapsed.TotalSeconds;
            if (remaining <= 0)
            {
                status = "Feasible; budget-exhausted before level=" + (stage - 1);
                break;
            }
            LinearExpr objective = objectives[stage];
            model.Add(objective <= costs[stage]);
            model.Minimize(objective);
            solver.StringParameters = "max_time_in_seconds:"
                + remaining.ToString("R", CultureInfo.InvariantCulture)
                + ",num_workers:1,random_seed:0";
            CpSolverStatus solved = solver.Solve(model);
            status = solved.ToString() + "; level=" + (stage - 1);
            if (solved != CpSolverStatus.Optimal && solved != CpSolverStatus.Feasible)
            {
                return hasSolution ? baseline : null;
            }
            hasSolution = true;
            baseline = ReadResult();
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
            for (int next = stage + 1; next < objectives.Length; next++)
            {
                costs[next] = solver.Value(objectives[next]);
            }
            if (solved != CpSolverStatus.Optimal)
            {
                break;
            }
            model.Add(objective == solver.Value(objective));
        }
        return hasSolution ? baseline : null;

        ContainerPackResult ReadResult()
        {
            var results = new List<PackResult>();
            for (int grid = 0; grid < requests.Count; grid++)
            {
                Placement[] placements = grids[grid]
                    .Where(r => solver.BooleanValue(r.Present))
                    .Select(r => new Placement(r.Item.Id, (int)solver.Value(r.X),
                        (int)solver.Value(r.Y), solver.BooleanValue(r.Rotated)))
                    .ToArray();
                var ids = new HashSet<string>(placements.Select(p => p.Id));
                results.Add(new PackResult(placements,
                    requests[grid].Items.Where(i => !ids.Contains(i.Id)).ToArray()));
            }
            return new ContainerPackResult(results);
        }
    }
}
