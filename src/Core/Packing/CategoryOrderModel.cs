using System.Collections.Generic;
using System.Linq;
using Google.OrTools.Sat;

namespace ChouUn.StashMaster.Core.Packing;

/// <summary>只约束类型中线的先后，保留共享行和全局重新落位的自由。</summary>
internal static class CategoryOrderModel
{
    internal static void Add(CpModel model, PackRequest request,
        IReadOnlyDictionary<PackingTree.Node, CategoryRangeModel.Range> ranges)
    {
        IReadOnlyList<CategoryOrder.Pair> pairs = CategoryOrder.Pairs(request);
        if (pairs.Count == 0) { return; }
        var roots = PackingTree.For(request).Roots.ToDictionary(node => node.SortType);
        foreach (CategoryOrder.Pair pair in pairs)
        {
            CategoryRangeModel.Range before = ranges[roots[pair.Before]];
            CategoryRangeModel.Range after = ranges[roots[pair.After]];
            model.Add(before.First + before.End <= after.First + after.End);
        }
    }
}
