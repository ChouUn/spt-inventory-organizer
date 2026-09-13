using System;
using System.Collections.Generic;
using System.Linq;
using Google.OrTools.Sat;

namespace ChouUn.StashMaster.Core.Packing;

/// <summary>树节点的精确覆盖范围；同一根范围同时服务中线顺序和跨度目标。</summary>
internal static class CategoryRangeModel
{
    internal readonly record struct Member(IntVar First, IntVar End);

    internal sealed record Range(IntVar First, IntVar End, LinearExpr Span);

    internal static IReadOnlyDictionary<PackingTree.Node, Range> AddGeometry(
        CpModel model, PackRequest request, IReadOnlyList<PackingTree.Node> nodes,
        IReadOnlyList<CpSatModel.Rectangle> rectangles,
        IReadOnlyDictionary<string, Placement> hints)
    {
        var tree = PackingTree.For(request);
        var sources = nodes.ToDictionary(node => node, _ => new List<Member>());
        foreach (CpSatModel.Rectangle rectangle in rectangles)
        {
            IntVar first = rectangle.Y;
            IntVar end = rectangle.EndY;
            if (!rectangle.Item.Required)
            {
                // 缺席矩形的原坐标不受区间约束，不能直接参与节点的 min/max。
                first = model.NewIntVar(0, request.Height, "member-first");
                end = model.NewIntVar(0, request.Height, "member-end");
                model.Add(first == rectangle.Y).OnlyEnforceIf(rectangle.Present);
                model.Add(first == request.Height).OnlyEnforceIf(rectangle.Present.Not());
                model.Add(end == rectangle.EndY).OnlyEnforceIf(rectangle.Present);
                model.Add(end == 0).OnlyEnforceIf(rectangle.Present.Not());
                bool present = hints.TryGetValue(rectangle.Item.Id, out Placement? placement);
                model.AddHint(first, present ? placement!.Y : request.Height);
                model.AddHint(end, present ? placement!.Y
                    + (placement.Rotated ? rectangle.Item.Width : rectangle.Item.Height) : 0);
            }
            IReadOnlyList<PackingTree.Node> path = tree.Paths[rectangle.Item.Id];
            for (int depth = path.Count - 1; depth >= 0; depth--)
            {
                if (!sources.TryGetValue(path[depth], out List<Member>? direct)) { continue; }
                direct.Add(new Member(first, end));
                break;
            }
        }
        return Add(model, request, nodes,
            node => sources[node], hints);
    }

    /// <summary>传入直属成员范围，自底向上复用子节点；缺席成员为 First=Height、End=0。</summary>
    internal static IReadOnlyDictionary<PackingTree.Node, Range> Add(
        CpModel model, PackRequest request, IReadOnlyList<PackingTree.Node> nodes,
        Func<PackingTree.Node, IReadOnlyList<Member>> source,
        IReadOnlyDictionary<string, Placement> hints, bool allPresent = false)
    {
        var ranges = new Dictionary<PackingTree.Node, Range>();
        var sources = nodes.ToDictionary(node => node, node => source(node).ToList());
        for (int index = nodes.Count - 1; index >= 0; index--)
        {
            PackingTree.Node node = nodes[index];
            bool required = allPresent || node.Items.Any(item => item.Required);
            IReadOnlyList<Member> members = sources[node];
            IntVar first = model.NewIntVar(0,
                required ? request.Height - 1 : request.Height, "tree-first");
            IntVar end = model.NewIntVar(required ? 1 : 0, request.Height, "tree-end");
            model.AddMinEquality(first, members.Select(member => member.First));
            model.AddMaxEquality(end, members.Select(member => member.End));
            LinearExpr span = end - first - 1;
            IntVar? nonnegative = null;
            if (!required)
            {
                // 全缺席时 End-First-1 为负；非空时恰为 inclusive bottom-first。
                nonnegative = model.NewIntVar(0, request.Height - 1, "tree-span");
                model.AddMaxEquality(nonnegative, new[] { span, LinearExpr.Constant(0) });
                span = nonnegative;
            }
            if (required)
            {
                int area = 0;
                int minHeight = 1;
                foreach (PackItem item in node.Items)
                {
                    if (!allPresent && !item.Required) { continue; }
                    area += item.Width * item.Height;
                    int height = item.Width <= request.Width ? item.Height : request.Height;
                    if (item.Height <= request.Width) { height = Math.Min(height, item.Width); }
                    minHeight = Math.Max(minHeight, height);
                }
                minHeight = Math.Max(minHeight, (area + request.Width - 1) / request.Width);
                model.Add(span >= minHeight - 1);
            }
            (int firstHint, int endHint) = PackingTree.Measure(node, hints);
            if (endHint == 0)
            {
                firstHint = required ? 0 : request.Height;
                endHint = required ? 1 : 0;
            }
            model.AddHint(first, firstHint);
            model.AddHint(end, endHint);
            if (nonnegative != null)
                model.AddHint(nonnegative, Math.Max(0, endHint - firstHint - 1));
            ranges.Add(node, new Range(first, end, span));
            if (node.Parent != null && sources.TryGetValue(node.Parent, out List<Member>? parent))
                parent.Add(new Member(first, end));
        }
        return ranges;
    }
}
