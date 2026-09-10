using System.Collections.Generic;
using System.Linq;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Packing;
using ChouUn.InventoryOrganizer.Core.Tags;

namespace ChouUn.InventoryOrganizer.Core.Organizing;

/// <summary>
/// 一个待排布的网格：所属容器、网格序号、装箱请求，以及参与排布的物品。
/// </summary>
public sealed record GridPackJob(
    ItemSnapshot Container,
    int GridIndex,
    PackRequest Request,
    IReadOnlyList<ItemSnapshot> FreeItems);

/// <summary>
/// 排布范围与收纳一致：根容器加所有带有效 tag 的容器；未打 tag 的容器内部不动。
/// Locked 的容器连同其内部一律跳过，游戏本就拒绝改动锁内的物品。
/// Pinned 与 Locked 的物品作为固定占位，只有 Free 物品参与排布。
/// </summary>
public static class PackPlanner
{
    public static IReadOnlyList<GridPackJob> Plan(ItemSnapshot root)
    {
        var jobs = new List<GridPackJob>();
        AddContainer(root, jobs);
        AddTaggedDescendants(root, jobs);
        return jobs;
    }

    private static void AddTaggedDescendants(
        ItemSnapshot container, List<GridPackJob> jobs)
    {
        foreach (ItemSnapshot child in container.Grids.SelectMany(grid => grid.Items))
        {
            if (child.Lock == LockState.Locked)
            {
                continue;
            }
            if (HasValidRules(child))
            {
                AddContainer(child, jobs);
            }
            AddTaggedDescendants(child, jobs);
        }
    }

    private static void AddContainer(ItemSnapshot container, List<GridPackJob> jobs)
    {
        foreach (GridSnapshot grid in container.Grids)
        {
            List<ItemSnapshot> free = grid.Items
                .Where(item => item.Lock == LockState.Free)
                .ToList();
            if (free.Count == 0)
            {
                continue;
            }
            List<FixedBlock> fixedBlocks = grid.Items
                .Where(item => item.Lock != LockState.Free)
                .Select(Footprint)
                .ToList();
            List<PackItem> packItems = free.Select(ToPackItem).ToList();
            var request = new PackRequest(
                grid.Width, grid.Height, fixedBlocks, packItems);
            jobs.Add(new GridPackJob(container, grid.Index, request, free));
        }
    }

    private static PackItem ToPackItem(ItemSnapshot item)
    {
        return new PackItem(item.Id, item.TemplateId, item.Width, item.Height);
    }

    private static FixedBlock Footprint(ItemSnapshot item)
    {
        GridPosition position = item.Position!;
        return position.Rotated
            ? new FixedBlock(position.X, position.Y, item.Height, item.Width)
            : new FixedBlock(position.X, position.Y, item.Width, item.Height);
    }

    private static bool HasValidRules(ItemSnapshot item)
    {
        if (item.Tag is null)
        {
            return false;
        }
        TagParseResult parsed = TagParser.Parse(item.Tag);
        return parsed.IsValid && parsed.Rules.Count > 0;
    }
}
