using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Google.OrTools.Sat;

namespace ChouUn.StashMaster.Core.Packing;

/// <summary>可转置矩形模型；单格只建数量变量，求解后用启发式填入空格。</summary>
internal static class CpSatModel
{
    public static PackResult? Solve(
        PackRequest request, PackResult baseline, double seconds, out string status)
    {
        var elapsed = Stopwatch.StartNew();
        var model = new CpModel();
        NoOverlap2dConstraint overlap = model.AddNoOverlap2D();
        foreach (FixedBlock block in request.Fixed)
        {
            overlap.AddRectangle(
                model.NewFixedSizeIntervalVar(
                    LinearExpr.Constant(block.X), block.Width, "fixed-x"),
                model.NewFixedSizeIntervalVar(
                    LinearExpr.Constant(block.Y), block.Height, "fixed-y"));
        }
        IntVar top = model.NewIntVar(0, request.Height, "movable-top");
        PackItem[] singles = request.Items.Where(i => i.Width == 1 && i.Height == 1)
            .OrderByDescending(i => i.Required)
            .ThenBy(i => i.TemplateId, StringComparer.Ordinal)
            .ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
        IntVar singleCount = model.NewIntVar(
            singles.Count(i => i.Required), singles.Length, "single-count");
        var variables = new List<Rectangle>();
        var terms = new List<LinearExpr> { singleCount };
        var hints = baseline.Placements.ToDictionary(p => p.Id);
        foreach (PackItem item in request.Items
            .Where(i => i.Width != 1 || i.Height != 1))
        {
            Rectangle rect = AddItem(model, overlap, request, item, top);
            variables.Add(rect);
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
        LinearExpr area = LinearExpr.Sum(terms);
        PackingSymmetry.Add(model, new[] { request }, variables.Select(r => (0, r)),
            new ContainerPackResult(new[] { baseline }));
        model.Add(area >= CpSatPacker.Area(request, baseline));
        // 只扣除 top 以内的障碍，底部固定容器不能使上方压实目标恒为仓库高度。
        IntVar available = model.NewIntVar(0,
            request.Width * request.Height, "available-cells");
        model.AddElement(top, CpSatPacker.AvailableCells(request), available);
        model.Add(area <= available);
        model.Maximize(area * (request.Height + 1) - top);
        model.AddHint(top, CpSatPacker.Height(request, baseline));
        model.AddHint(singleCount, singles.Count(i => hints.ContainsKey(i.Id)));
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
        Placement[] large = variables.Where(v => solver.BooleanValue(v.Present))
            .Select(v => new Placement(v.Item.Id, (int)solver.Value(v.X),
                (int)solver.Value(v.Y), solver.BooleanValue(v.Rotated))).ToArray();
        int count = (int)solver.Value(singleCount);
        var fillRequest = new PackRequest(request.Width, (int)solver.Value(top),
            request.Fixed.Concat(CpSatPacker.Blocks(request, large)).ToArray(),
            singles.Take(count).ToArray());
        PackResult filled = new HeuristicPacker().Pack(fillRequest);
        Placement[] placements = large.Concat(filled.Placements).ToArray();
        var placedIds = new HashSet<string>(placements.Select(p => p.Id));
        return new PackResult(placements,
            request.Items.Where(i => !placedIds.Contains(i.Id)).ToArray());
    }

    /// <summary>缺席矩形不占格；必留物品强制在场，位置和转置受网格边界约束。</summary>
    internal static Rectangle AddItem(
        CpModel model, NoOverlap2dConstraint overlap, PackRequest request,
        PackItem item, IntVar top)
    {
        BoolVar present = model.NewBoolVar(item.Id + "-present");
        BoolVar rotated = model.NewBoolVar(item.Id + "-rotated");
        if (item.Required)
        {
            model.Add(present == 1);
        }
        if (item.Width == item.Height)
        {
            model.Add(rotated == 0);
        }
        IntVar x = model.NewIntVar(0, request.Width, item.Id + "-x");
        IntVar y = model.NewIntVar(0, request.Height, item.Id + "-y");
        LinearExpr width = item.Width + rotated * (item.Height - item.Width);
        LinearExpr height = item.Height + rotated * (item.Width - item.Height);
        // 缺席的大件可以比网格大，不能让其区间端点反过来导致整个模型不可行。
        IntVar endX = model.NewIntVar(0,
            request.Width + Math.Max(item.Width, item.Height), item.Id + "-end-x");
        IntVar endY = model.NewIntVar(0,
            request.Height + Math.Max(item.Width, item.Height), item.Id + "-end-y");
        model.Add(endX <= request.Width).OnlyEnforceIf(present);
        model.Add(endY <= top).OnlyEnforceIf(present);
        overlap.AddRectangle(
            model.NewOptionalIntervalVar(x, width, endX, present, item.Id + "-ix"),
            model.NewOptionalIntervalVar(y, height, endY, present, item.Id + "-iy"));
        return new Rectangle(item, x, y, rotated, present, endX, endY);
    }

    internal sealed record Rectangle(
        PackItem Item, IntVar X, IntVar Y, BoolVar Rotated, BoolVar Present,
        IntVar EndX, IntVar EndY);
}
