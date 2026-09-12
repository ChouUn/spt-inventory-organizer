using System.Collections.Generic;
using System.Linq;
using Google.OrTools.Sat;

namespace ChouUn.StashMaster.Core.Packing;

/// <summary>只约束类型中线的先后，保留共享行和全局重新落位的自由。</summary>
internal static class CategoryOrderModel
{
    internal static void Add(CpModel model,
        IReadOnlyList<PackRequest> requests,
        IReadOnlyList<List<CpSatModel.Rectangle>> grids,
        ContainerPackResult layoutHints)
    {
        for (int grid = 0; grid < requests.Count; grid++)
        {
            PackRequest request = requests[grid];
            IReadOnlyList<CategoryOrder.Pair> pairs = CategoryOrder.Pairs(request);
            if (pairs.Count == 0) { continue; }
            var hints = layoutHints.Grids[grid].Placements.ToDictionary(p => p.Id);
            var centers = new Dictionary<string, LinearExpr>();
            foreach (string id in pairs.SelectMany(p => new[] { p.Before, p.After })
                .Distinct())
            {
                var members = grids[grid].Where(r => r.Item.SortType == id)
                    .ToArray();
                IntVar first = model.NewIntVar(0, request.Height - 1, "order-first");
                IntVar end = model.NewIntVar(1, request.Height, "order-end");
                model.AddMinEquality(first, members.Select(r => r.Y));
                model.AddMaxEquality(end, members.Select(r => r.EndY));
                var placed = members.Where(r => hints.ContainsKey(r.Item.Id)).ToArray();
                int firstHint = placed.Select(r => hints[r.Item.Id].Y)
                    .DefaultIfEmpty().Min();
                int endHint = placed.Select(r => hints[r.Item.Id].Y
                    + (hints[r.Item.Id].Rotated ? r.Item.Width : r.Item.Height))
                    .DefaultIfEmpty(1).Max();
                model.AddHint(first, firstHint);
                model.AddHint(end, endHint);
                centers.Add(id, first + end);
            }
            foreach (CategoryOrder.Pair pair in pairs)
            {
                model.Add(centers[pair.Before] <= centers[pair.After]);
            }
        }
    }
}
