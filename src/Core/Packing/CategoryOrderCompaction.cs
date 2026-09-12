using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Google.OrTools.Sat;

namespace ChouUn.StashMaster.Core.Packing;

/// <summary>在已排序布局附近尝试压掉一行，为后续全局聚合提供紧凑提示。</summary>
internal static class CategoryOrderCompaction
{
    // 当前启发式参数的验证问题见 docs/plans/type-order-tuning.md。
    private const int MaxVerticalShift = 3;
    private const int MaxWorkers = 8;

    internal static PackResult TryCompact(PackRequest request, PackResult baseline,
        double seconds)
    {
        var model = new CpModel();
        NoOverlap2dConstraint overlap = model.AddNoOverlap2D();
        foreach (FixedBlock block in request.Fixed)
        {
            overlap.AddRectangle(model.NewFixedSizeIntervalVar(
                LinearExpr.Constant(block.X), block.Width, "fixed-x"),
                model.NewFixedSizeIntervalVar(
                    LinearExpr.Constant(block.Y), block.Height, "fixed-y"));
        }
        IntVar top = model.NewIntVar(0,
            CpSatPacker.Height(request, baseline) - 1, "compact-top");
        var rectangles = new List<CpSatModel.Rectangle>();
        var before = baseline.Placements.ToDictionary(p => p.Id);
        foreach (PackItem item in request.Items)
        {
            CpSatModel.Rectangle rect =
                CpSatModel.AddItem(model, overlap, request, item, top);
            Placement p = before[item.Id];
            model.Add(rect.Y >= Math.Max(0, p.Y - MaxVerticalShift));
            model.Add(rect.Y <= p.Y + MaxVerticalShift);
            model.AddHint(rect.X, p.X);
            model.AddHint(rect.Y, p.Y);
            model.AddHint(rect.Rotated, p.Rotated);
            rectangles.Add(rect);
        }
        var hint = new ContainerPackResult(new[] { baseline });
        CategoryOrderModel.Add(model, new[] { request }, new[] { rectangles }, hint);
        // 此阶段只求几何压紧；真实模板与手册聚合仍交给后续全局模型。
        PackItem[][] groups = request.Items
            .GroupBy(i => (i.SortType, i.Width, i.Height))
            .Select(g => g.OrderBy(i => i.Id, StringComparer.Ordinal).ToArray())
            .ToArray();
        PackingSymmetry.Add(model, groups,
            rectangles.Select(r => (0, r)), hint);
        using var solver = new CpSolver();
        // 多种搜索策略并行寻找可行解；此阶段只提供提示，不作最优性承诺。
        solver.StringParameters = "max_time_in_seconds:"
            + seconds.ToString("R", CultureInfo.InvariantCulture)
            + ",num_workers:" + Math.Min(MaxWorkers, Environment.ProcessorCount)
            + ",random_seed:0";
        var elapsed = Stopwatch.StartNew();
        CpSolverStatus status = solver.Solve(model);
        string diagnostic = baseline.Diagnostic
            + $"; order-compact={status}:{elapsed.ElapsedMilliseconds}ms";
        if (status != CpSolverStatus.Feasible && status != CpSolverStatus.Optimal)
            return baseline with { Diagnostic = diagnostic };
        return new PackResult(rectangles.Select(r => new Placement(r.Item.Id,
            (int)solver.Value(r.X), (int)solver.Value(r.Y),
            solver.Value(r.Rotated) != 0)).ToArray(), Array.Empty<PackItem>())
        {
            Diagnostic = diagnostic,
        };
    }
}
