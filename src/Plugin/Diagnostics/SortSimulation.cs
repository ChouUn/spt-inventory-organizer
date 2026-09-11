#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using ChouUn.StashMaster.Core.Organizing;
using ChouUn.StashMaster.Core.Packing;
using EFT.InventoryLogic;

namespace ChouUn.StashMaster.Diagnostics;

/// <summary>
/// 在独立占用缓冲上执行列表排序和原生的五次回退落位流程。
/// 不调用物品增删、控制器或事务；物品地址、堆叠和真实网格完全不写入。
/// </summary>
internal static class SortSimulation
{
    internal static PackResult Run(GridPackJob job,
        IReadOnlyDictionary<string, Item> items,
        Func<IEnumerable<Item>, List<Item>> sort,
        Func<Grid, Item, LocationInGrid?> find)
    {
        PackRequest request = job.Request;
        // 同一网格、同一物品集合与固定障碍；只比较排布计算，不跨网格搬运。
        var grid = new Grid("benchmark", request.Width, request.Height, false,
            false, Array.Empty<ItemFilter>(), null!);
        foreach (FixedBlock block in request.Fixed)
        {
            grid.SetLayout(new IntVec2(block.Width, block.Height),
                new LocationInGrid(block.X, block.Y, ItemRotation.Horizontal),
                value: true, stretch: false);
        }
        grid.FillSpaceBuffer();
        List<Item> remaining = sort(job.FreeItems.Select(i => items[i.Id]));
        var placed = new List<(Item Item, LocationInGrid Location)>();
        int retries = 5;
        while (remaining.Count > 0 && retries > 0)
        {
            Item item = remaining[0];
            remaining.RemoveAt(0);
            LocationInGrid? location = find(grid, item);
            if (location == null)
            {
                retries--;
                while (placed.Count > 0)
                {
                    var last = placed[placed.Count - 1];
                    placed.RemoveAt(placed.Count - 1);
                    Occupy(last.Item, last.Location, false);
                    remaining.Insert(0, last.Item);
                    location = find(grid, item);
                    if (location != null) { break; }
                }
            }
            if (location == null)
            {
                remaining.Insert(0, item);
                break;
            }
            Occupy(item, location, true);
            placed.Add((item, location));
        }
        var result = new PackResult(placed.Select(p => new Placement(
            p.Item.Id, p.Location.x, p.Location.y,
            p.Location.r == ItemRotation.Vertical)).ToArray(),
            request.Items.Where(i => remaining.Any(r => r.Id == i.Id)).ToArray());
        return result;

        void Occupy(Item item, LocationInGrid location, bool value)
        {
            grid.SetLayout(item, location, value, stretch: false);
            grid.FillSpaceBuffer();
        }
    }
}
#endif
