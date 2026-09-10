using System.Collections.Generic;
using System.Linq;
using Google.OrTools.Sat;

namespace ChouUn.InventoryOrganizer.Core.Packing;

/// <summary>等价位置槽按网格、行列排序，缺席槽在末尾，消除身份互换的重复解。</summary>
internal static class PackingSymmetry
{
    public static void Add(CpModel model, IReadOnlyList<PackRequest> requests,
        IEnumerable<(int Grid, CpSatModel.Rectangle Rect)> rectangles,
        ContainerPackResult baseline)
    {
        var byId = rectangles.GroupBy(r => r.Rect.Item.Id)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var hints = baseline.Grids.SelectMany((r, grid) => r.Placements.Select(p =>
            (p.Id, Placement: p, Grid: grid))).ToDictionary(p => p.Id);
        foreach (PackItem[] members in PackingIdentity.Groups(requests))
        {
            PackItem[] group = members
                .OrderBy(i => hints.TryGetValue(i.Id, out var p)
                    ? p.Grid : int.MaxValue)
                .ThenBy(i => hints.TryGetValue(i.Id, out var p) ? p.Placement.Y : 0)
                .ThenBy(i => hints.TryGetValue(i.Id, out var p) ? p.Placement.X : 0)
                .ToArray();
            if (group.Length < 2 || !byId.ContainsKey(group[0].Id)) { continue; }
            for (int i = 1; i < group.Length; i++)
            {
                var previous = byId[group[i - 1].Id];
                var next = byId[group[i].Id];
                model.Add(LinearExpr.Sum(previous.Select(v => v.Rect.Present))
                    >= LinearExpr.Sum(next.Select(v => v.Rect.Present)));
                foreach (var a in previous)
                {
                    foreach (var b in next)
                    {
                        if (a.Grid > b.Grid)
                        {
                            model.AddBoolOr(new ILiteral[]
                                { a.Rect.Present.Not(), b.Rect.Present.Not() });
                        }
                        else if (a.Grid == b.Grid)
                        {
                            CpSatModel.Rectangle left = a.Rect;
                            CpSatModel.Rectangle right = b.Rect;
                            model.Add(left.Y <= right.Y)
                                .OnlyEnforceIf(new ILiteral[]
                                    { left.Present, right.Present });
                            BoolVar sameRow = model.NewBoolVar("same-row");
                            model.Add(left.Y == right.Y).OnlyEnforceIf(sameRow);
                            model.Add(left.Y != right.Y).OnlyEnforceIf(sameRow.Not());
                            model.Add(left.X <= right.X).OnlyEnforceIf(new ILiteral[]
                                { left.Present, right.Present, sameRow });
                            int firstY = hints.TryGetValue(left.Item.Id, out var p)
                                && p.Grid == a.Grid ? p.Placement.Y : 0;
                            int nextY = hints.TryGetValue(right.Item.Id, out var q)
                                && q.Grid == b.Grid ? q.Placement.Y : 0;
                            model.AddHint(sameRow, firstY == nextY);
                        }
                    }
                }
            }
        }
    }
}
