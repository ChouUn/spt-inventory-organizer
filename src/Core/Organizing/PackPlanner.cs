using System.Collections.Generic;
using System.Linq;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Packing;

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
    public static IReadOnlyList<GridPackJob> Plan(ItemSnapshot root,
        System.Func<string, Tags.TagParseResult>? parse = null)
    {
        var jobs = new List<GridPackJob>();
        foreach (ItemSnapshot container in OrganizeScope.Containers(root, parse))
        {
            AddContainer(container, jobs);
        }
        return jobs;
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
            jobs.Add(ForGrid(container, grid));
        }
    }

    /// <summary>现有物品必留，当前位置提供保底；收纳规划可以追加可选候选。</summary>
    public static GridPackJob ForGrid(ItemSnapshot container, GridSnapshot grid)
    {
        List<ItemSnapshot> free = grid.Items.Where(i => i.Lock == LockState.Free)
            .ToList();
        List<FixedBlock> fixedBlocks = grid.Items
            .Where(item => item.Lock != LockState.Free)
            .Select(Footprint)
            .ToList();
        List<PackItem> packItems = free.Select(ToPackItem).ToList();
        var request = new PackRequest(grid.Width, grid.Height, fixedBlocks, packItems)
        {
            Current = free.Select(i => new Placement(
                i.Id, i.Position!.X, i.Position.Y, i.Position.Rotated)).ToArray(),
        };
        return new GridPackJob(container, grid.Index, request, free);
    }

    private static PackItem ToPackItem(ItemSnapshot item)
    {
        return new PackItem(item.Id, item.TemplateId, item.Width, item.Height)
        {
            Required = true,
            CategoryPath = item.CategoryPath,
        };
    }

    private static FixedBlock Footprint(ItemSnapshot item)
    {
        GridPosition position = item.Position!;
        return position.Rotated
            ? new FixedBlock(position.X, position.Y, item.Height, item.Width)
            : new FixedBlock(position.X, position.Y, item.Width, item.Height);
    }
}
