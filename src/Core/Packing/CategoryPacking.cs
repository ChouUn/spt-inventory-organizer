using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ChouUn.StashMaster.Core.Packing;

/// <summary>逐层统计覆盖行跨度，父层优先；固定障碍与未知类别不计入。</summary>
public static class CategoryPacking
{
    /// <summary>构造空间不退步的层级提示，不限制全局模型后续搜索。</summary>
    internal static PackResult Seed(PackRequest request, PackResult baseline)
    {
        int depth = Depth(request);
        foreach (bool reverse in new[] { false, true })
        {
            var ranks = Enumerable.Range(0, depth).Select(level =>
            {
                var groups = Groups(request, level)
                    .OrderByDescending(g => g.Sum(i => i.Width * i.Height))
                    .ThenBy(g => g.Key, StringComparer.Ordinal).ToArray();
                return (reverse ? groups.Reverse() : groups)
                    .Select((g, index) => (g.Key, index))
                    .ToDictionary(p => p.Key, p => p.index);
            }).ToArray();
            var keys = request.Items.ToDictionary(i => i.Id, i => string.Join("/",
                i.CategoryPath.Select((id, level) => ranks[level][id].ToString("D5"))));
            // 比较按完整类别链、先大件以及父类内部先大件三种合法提示。
            for (int mode = 0; mode < 3; mode++)
            {
                PackResult seed = HeuristicPacker.PackOrdered(request, request.Items
                    .OrderBy(i => mode == 1 && i.Width * i.Height == 1)
                    .ThenBy(i => mode == 2 && i.CategoryPath.Count > 0
                        ? ranks[0][i.CategoryPath[0]] : 0)
                    .ThenBy(i => mode == 2 && i.Width * i.Height == 1)
                    .ThenBy(i => keys[i.Id], StringComparer.Ordinal)
                    .ThenByDescending(i => i.Width * i.Height)
                    .ThenBy(i => i.TemplateId, StringComparer.Ordinal)
                    .ThenBy(i => i.Id, StringComparer.Ordinal));
                if (CpSatPacker.Better(request, seed, baseline))
                {
                    baseline = seed;
                }
            }
        }
        return baseline;
    }

    public static int Span(PackRequest request, PackResult result, int level = 0)
    {
        var items = request.Items.ToDictionary(i => i.Id);
        return result.Placements.Where(p => items[p.Id].CategoryPath.Count > level)
            .GroupBy(p => items[p.Id].CategoryPath[level])
            .Sum(g => g.Max(p => p.Y + (p.Rotated
                ? items[p.Id].Width : items[p.Id].Height) - 1) - g.Min(p => p.Y));
    }

    public static int[] Spans(PackRequest request, PackResult result) =>
        Enumerable.Range(0, Depth(request)).Select(d => Span(request, result, d))
            .ToArray();

    /// <summary>先算各网格完整分数，再求和；与联合模型使用相同目标。</summary>
    internal static int Compare(IReadOnlyList<PackRequest> requests,
        ContainerPackResult next, ContainerPackResult before)
        => requests.Select((r, i) =>
                Score(r, next.Grids[i]) - Score(r, before.Grids[i]))
            .Aggregate(BigInteger.Zero, (sum, difference) => sum + difference).Sign;

    internal static int Compare(PackRequest request, PackResult next, PackResult before)
    {
        for (int d = 0; d < Depth(request); d++)
        {
            int difference = Span(request, next, d) - Span(request, before, d);
            if (difference != 0) { return difference; }
        }
        return 0;
    }

    /// <summary>仅用本网格的类别上界确定倍率；任意精度避免深层类别溢出。</summary>
    internal static BigInteger Score(PackRequest request, PackResult result)
    {
        BigInteger score = CpSatPacker.Height(request, result)
            + (long)(AreaUpperBound(request) - CpSatPacker.Area(request, result))
                * (request.Height + 1L);
        for (int level = 0; level < Depth(request); level++)
        {
            score = score * (UpperBound(request, level) + 1)
                + Span(request, result, level);
        }
        return score;
    }

    internal static long UpperBound(PackRequest request, int level) =>
        (request.Height - 1L) * Groups(request, level).Count();

    /// <summary>
    /// 由必留面积和其他类别的最大供给量推导每类不可避免的面积。
    /// 可选物品可被舍弃；几何放置暂时放宽，因此是安全下界而非布局承诺。
    /// </summary>
    internal static int LowerBound(PackRequest request, int area, int level)
    {
        int total = request.Items.Sum(i => i.Width * i.Height);
        return Groups(request, level).Sum(group =>
        {
            int needed = Math.Max(group.Where(i => i.Required)
                .Sum(i => i.Width * i.Height),
                area - (total - group.Sum(i => i.Width * i.Height)));
            int rows = Math.Max((needed + request.Width - 1) / request.Width,
                group.Where(i => i.Required).Select(i => Math.Min(i.Width, i.Height))
                    .DefaultIfEmpty().Max());
            return Math.Max(0, rows - 1);
        });
    }

    internal static int AreaUpperBound(PackRequest request) => Math.Min(
        request.Items.Sum(i => i.Width * i.Height),
        (int)CpSatPacker.AvailableCells(request)[request.Height]);

    internal static int Depth(PackRequest request) => request.Items
        .Select(i => i.CategoryPath.Count).DefaultIfEmpty().Max();

    internal static IEnumerable<IGrouping<string, PackItem>> Groups(
        PackRequest request, int level) => request.Items
        .Where(i => i.CategoryPath.Count > level).GroupBy(i => i.CategoryPath[level]);

    internal static bool HasCategories(IEnumerable<PackRequest> requests) =>
        requests.Any(r => Depth(r) > 0);

    internal static string Describe(PackRequest request, PackResult result) =>
        "[" + string.Join(",", Spans(request, result)) + "]";
}
