using System;
using System.Collections.Generic;
using System.Linq;

namespace ChouUn.StashMaster.Core.Packing;

/// <summary>等价组使用匿名位置槽，布局完成后按原位优先映射真实身份。</summary>
internal sealed class PackingIdentity
{
    private readonly IReadOnlyList<PackRequest> _original;
    private readonly List<(PackItem[] Items, string[] Slots)> _groups = new();

    public PackingIdentity(IReadOnlyList<PackRequest> requests)
    {
        _original = requests;
        var slotsById = new Dictionary<string, string>();
        var current = requests.SelectMany((r, grid) => r.Current.Select(p =>
            (p.Id, Position: new Position(grid, p.X, p.Y, p.Rotated))))
            .ToDictionary(p => p.Id, p => p.Position);
        foreach (PackItem[] group in Groups(requests))
        {
            PackItem[] ordered = group.OrderBy(i => current.ContainsKey(i.Id) ? 0 : 1)
                .ThenBy(i => current.TryGetValue(i.Id, out Position? p) ? p.Grid : 0)
                .ThenBy(i => current.TryGetValue(i.Id, out Position? p) ? p.Y : 0)
                .ThenBy(i => current.TryGetValue(i.Id, out Position? p) ? p.X : 0)
                .ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
            string[] slots = Enumerable.Range(0, ordered.Length)
                .Select(i => $"group-{_groups.Count:D5}-{i:D5}").ToArray();
            _groups.Add((ordered, slots));
            for (int i = 0; i < ordered.Length; i++)
                slotsById.Add(ordered[i].Id, slots[i]);
        }
        Requests = requests.Select(r => r with
        {
            Items = r.Items.Select(i => i with { Id = slotsById[i.Id] }).ToArray(),
            Current = r.Current.Select(p => p with { Id = slotsById[p.Id] })
                .OrderBy(p => p.Id, StringComparer.Ordinal).ToArray(),
        }).ToArray();
    }

    public IReadOnlyList<PackRequest> Requests { get; }

    public ContainerPackResult Restore(ContainerPackResult result)
    {
        var targets = result.Grids.SelectMany((r, grid) => r.Placements.Select(p =>
            (p.Id, Position: new Position(grid, p.X, p.Y, p.Rotated))))
            .ToDictionary(p => p.Id, p => p.Position);
        var current = _original.SelectMany((r, grid) => r.Current.Select(p =>
            (p.Id, Position: new Position(grid, p.X, p.Y, p.Rotated))))
            .ToDictionary(p => p.Id, p => p.Position);
        var identities = new Dictionary<string, string>();
        foreach (var group in _groups)
        {
            var free = group.Slots.Where(targets.ContainsKey)
                .ToDictionary(id => targets[id], id => id);
            var used = new HashSet<string>();
            // 先匹配全部原位，避免较早处理的移位物品抢走别人的原位。
            foreach (PackItem item in group.Items)
            {
                if (current.TryGetValue(item.Id, out Position? position)
                    && free.TryGetValue(position, out string? slot))
                {
                    identities.Add(slot, item.Id);
                    free.Remove(position);
                    used.Add(item.Id);
                }
            }
            var remaining = new Queue<PackItem>(group.Items
                .Where(i => !used.Contains(i.Id)));
            foreach (string slot in group.Slots.Where(id =>
                targets.ContainsKey(id) && !identities.ContainsKey(id)))
            {
                identities.Add(slot, remaining.Dequeue().Id);
            }
        }
        return result with
        {
            Diagnostic = result.Diagnostic + "; identity-mapping=original-first",
            Grids = result.Grids.Select((r, grid) =>
            {
                Placement[] placed = r.Placements
                    .Select(p => p with { Id = identities[p.Id] }).ToArray();
                var positions = placed.ToDictionary(p => p.Id);
                if (placed.Length == _original[grid].Current.Count
                    && _original[grid].Current.All(p =>
                        positions.TryGetValue(p.Id, out Placement? next) && next == p))
                {
                    placed = _original[grid].Current.ToArray();
                }
                var ids = new HashSet<string>(placed.Select(p => p.Id));
                var currentIds = _original[grid].Current.ToDictionary(p => p.Id);
                int changed = placed.Count(p =>
                    !currentIds.TryGetValue(p.Id, out Placement? old) || old != p);
                return r with
                {
                    Placements = placed,
                    Unplaced = _original[grid].Items
                        .Where(i => !ids.Contains(i.Id)).ToArray(),
                    Diagnostic = r.Diagnostic + $"; mapped-changed={changed}",
                };
            }).ToArray(),
        };
    }

    /// <summary>同组约束一致；长度编码避免类别链或模板文本的分隔符碰撞。</summary>
    internal static IReadOnlyList<PackItem[]> Groups(
        IReadOnlyList<PackRequest> requests)
    {
        return requests.SelectMany((r, grid) => r.Items.Select(i => (Item: i, grid)))
            .GroupBy(p => p.Item.Id).Select(g =>
            {
                PackItem item = g.First().Item;
                string key = Encode(item.TemplateId) + $"{item.Width},{item.Height};"
                    + Encode(item.SortType)
                    + Encode(string.Concat(item.SubcategoryPath.Select(Encode)))
                    + string.Join(";", g.OrderBy(p => p.grid)
                        .Select(p => $"{p.grid}:{p.Item.Required}"));
                return (Item: item, Key: key);
            }).GroupBy(p => p.Key).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Select(p => p.Item)
                .OrderBy(i => i.Id, StringComparer.Ordinal).ToArray()).ToArray();
    }

    private static string Encode(string value) => value.Length + ":" + value;

    private sealed record Position(int Grid, int X, int Y, bool Rotated);
}
