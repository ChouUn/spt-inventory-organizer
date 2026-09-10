using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Google.OrTools.Sat;

namespace ChouUn.InventoryOrganizer.Core.Packing;

/// <summary>各网格独立避让，共同约束物品唯一归属；单格按允许网格集合计数。</summary>
internal static class MultiGridModel
{
    public static ContainerPackResult? Solve(IReadOnlyList<PackRequest> requests,
        ContainerPackResult baseline, double seconds, out string status)
    {
        var elapsed = Stopwatch.StartNew();
        var model = new CpModel();
        var rectangles = new List<(int Grid, CpSatModel.Rectangle Rect)>();
        var tops = new List<IntVar>();
        var areas = new List<LinearExpr>();
        var singleGroups = Singles(requests);
        var counts = new Dictionary<(int Grid, int Group), IntVar>();
        for (int grid = 0; grid < requests.Count; grid++)
        {
            PackRequest request = requests[grid];
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
            var terms = new List<LinearExpr>();
            var hints = baseline.Grids[grid].Placements.ToDictionary(p => p.Id);
            foreach (PackItem item in request.Items
                .Where(i => i.Width != 1 || i.Height != 1))
            {
                CpSatModel.Rectangle rect =
                    CpSatModel.AddItem(model, overlap, request, item, top);
                rectangles.Add((grid, rect));
                terms.Add(rect.Present * (item.Width * item.Height));
                bool present = hints.TryGetValue(item.Id, out Placement? p);
                model.AddHint(rect.Present, present);
                if (present)
                {
                    model.AddHint(rect.X, p!.X);
                    model.AddHint(rect.Y, p.Y);
                    model.AddHint(rect.Rotated, p.Rotated);
                }
            }
            for (int group = 0; group < singleGroups.Count; group++)
            {
                SingleGroup singles = singleGroups[group];
                if (!singles.Grids.Contains(grid))
                {
                    continue;
                }
                IntVar count = model.NewIntVar(0, singles.Items.Length, "singles");
                counts.Add((grid, group), count);
                terms.Add(count);
                model.AddHint(count, singles.Items.Count(i => hints.ContainsKey(i.Id)));
            }
            LinearExpr area = LinearExpr.Sum(terms);
            areas.Add(area);
            IntVar available = model.NewIntVar(0,
                request.Width * request.Height, "available");
            model.AddElement(top, CpSatPacker.AvailableCells(request), available);
            model.Add(area <= available);
            model.AddHint(top, CpSatPacker.Height(request, baseline.Grids[grid]));
        }
        foreach (var item in rectangles.GroupBy(r => r.Rect.Item.Id))
        {
            model.Add(LinearExpr.Sum(item.Select(r => r.Rect.Present)) <= 1);
        }
        for (int group = 0; group < singleGroups.Count; group++)
        {
            SingleGroup singles = singleGroups[group];
            LinearExpr total = LinearExpr.Sum(
                singles.Grids.Select(g => counts[(g, group)]));
            model.Add(total <= singles.Items.Length);
            if (singles.Required)
            {
                model.Add(total == singles.Items.Length);
            }
        }
        LinearExpr allArea = LinearExpr.Sum(areas);
        model.Add(allArea >= ContainerPacker.Area(requests, baseline));
        model.Maximize(allArea * (requests.Sum(r => r.Height) + 1)
            - LinearExpr.Sum(tops));
        double remaining = seconds - elapsed.Elapsed.TotalSeconds;
        if (remaining <= 0)
        {
            status = "budget exhausted during model construction";
            return null;
        }
        using var solver = new CpSolver
        {
            StringParameters = "max_time_in_seconds:"
                + remaining.ToString("R", CultureInfo.InvariantCulture)
                + ",num_workers:1,random_seed:0",
        };
        CpSolverStatus solved = solver.Solve(model);
        status = solved.ToString();
        if (solved != CpSolverStatus.Optimal && solved != CpSolverStatus.Feasible)
        {
            return null;
        }
        var results = new List<PackResult>();
        var offsets = new int[singleGroups.Count];
        for (int grid = 0; grid < requests.Count; grid++)
        {
            PackRequest request = requests[grid];
            Placement[] large = rectangles.Where(r => r.Grid == grid
                && solver.BooleanValue(r.Rect.Present)).Select(r =>
                    new Placement(r.Rect.Item.Id, (int)solver.Value(r.Rect.X),
                        (int)solver.Value(r.Rect.Y),
                        solver.BooleanValue(r.Rect.Rotated))).ToArray();
            var singles = new List<PackItem>();
            for (int group = 0; group < singleGroups.Count; group++)
            {
                if (counts.TryGetValue((grid, group), out IntVar? variable))
                {
                    int count = (int)solver.Value(variable);
                    singles.AddRange(singleGroups[group].Items
                        .Skip(offsets[group]).Take(count));
                    offsets[group] += count;
                }
            }
            var fill = new PackRequest(request.Width, (int)solver.Value(tops[grid]),
                request.Fixed.Concat(CpSatPacker.Blocks(request, large)).ToArray(),
                singles);
            Placement[] placements = large.Concat(
                new HeuristicPacker().Pack(fill).Placements).ToArray();
            var ids = new HashSet<string>(placements.Select(p => p.Id));
            results.Add(new PackResult(placements,
                request.Items.Where(i => !ids.Contains(i.Id)).ToArray()));
        }
        return new ContainerPackResult(results);
    }

    /// <summary>同组单格可互换；不同过滤集合或必留网格必须分别计数。</summary>
    private static IReadOnlyList<SingleGroup> Singles(
        IReadOnlyList<PackRequest> requests)
    {
        return requests.SelectMany((r, grid) => r.Items
                .Where(i => i.Width == 1 && i.Height == 1).Select(i => (Item: i, grid)))
            .GroupBy(p => p.Item.Id)
            .Select(g => new
            {
                Item = g.First().Item,
                Grids = g.Select(p => p.grid).ToArray(),
            })
            .GroupBy(p => (p.Item.Required, Key: string.Join(",", p.Grids)))
            .Select(g => new SingleGroup(g.First().Grids,
                g.Select(p => p.Item).OrderBy(i => i.TemplateId, StringComparer.Ordinal)
                    .ThenBy(i => i.Id, StringComparer.Ordinal).ToArray(),
                g.Key.Required)).ToArray();
    }

    private sealed record SingleGroup(int[] Grids, PackItem[] Items, bool Required);
}
