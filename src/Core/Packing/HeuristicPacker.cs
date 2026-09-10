using System;
using System.Collections.Generic;
using System.Linq;

namespace ChouUn.InventoryOrganizer.Core.Packing;

/// <summary>
/// 首次适配递减：大件先放，逐格扫描第一个能放的位置。
/// 同尺寸的物品按模板相邻，让同类物品挨在一起；这是零成本的分组手段。
/// </summary>
public sealed class HeuristicPacker : IPacker
{
    public PackResult Pack(PackRequest request, double maxSeconds = 1)
        => PackOrdered(request, Order(request.Items));

    /// <summary>按给定次序构造合法提示，最终仍比较面积、高度和聚合。</summary>
    internal static PackResult PackOrdered(PackRequest request,
        IEnumerable<PackItem> order)
    {
        var occupied = new bool[request.Width, request.Height];
        foreach (FixedBlock block in request.Fixed)
        {
            Mark(occupied, block.X, block.Y, block.Width, block.Height);
        }

        var placements = new List<Placement>();
        var unplaced = new List<PackItem>();
        foreach (PackItem item in order)
        {
            if (TryPlace(occupied, item, out Placement placement))
            {
                placements.Add(placement);
            }
            else
            {
                unplaced.Add(item);
            }
        }
        return new PackResult(placements, unplaced);
    }

    private static IEnumerable<PackItem> Order(IReadOnlyList<PackItem> items)
    {
        return items
            .OrderByDescending(item => item.Width * item.Height)
            .ThenByDescending(item => Math.Max(item.Width, item.Height))
            .ThenBy(item => item.TemplateId, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal);
    }

    private static bool TryPlace(
        bool[,] occupied, PackItem item, out Placement placement)
    {
        int width = occupied.GetLength(0);
        int height = occupied.GetLength(1);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (Fits(occupied, x, y, item.Width, item.Height))
                {
                    Mark(occupied, x, y, item.Width, item.Height);
                    placement = new Placement(item.Id, x, y, false);
                    return true;
                }
                bool rotatable = item.Width != item.Height;
                if (rotatable && Fits(occupied, x, y, item.Height, item.Width))
                {
                    Mark(occupied, x, y, item.Height, item.Width);
                    placement = new Placement(item.Id, x, y, true);
                    return true;
                }
            }
        }
        placement = null!;
        return false;
    }

    private static bool Fits(bool[,] occupied, int x, int y, int width, int height)
    {
        if (x + width > occupied.GetLength(0) || y + height > occupied.GetLength(1))
        {
            return false;
        }
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                if (occupied[x + dx, y + dy])
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static void Mark(bool[,] occupied, int x, int y, int width, int height)
    {
        int maxX = Math.Min(x + width, occupied.GetLength(0));
        int maxY = Math.Min(y + height, occupied.GetLength(1));
        for (int cx = Math.Max(x, 0); cx < maxX; cx++)
        {
            for (int cy = Math.Max(y, 0); cy < maxY; cy++)
            {
                occupied[cx, cy] = true;
            }
        }
    }
}
