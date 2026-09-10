using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ChouUn.InventoryOrganizer.Core.Packing;

/// <summary>每项对应一个网格；同一可选物品可出现在多个允许进入的网格中。</summary>
public sealed record ContainerPackResult(IReadOnlyList<PackResult> Grids)
{
    public string Diagnostic { get; init; } = "container baseline";
    public string? Warning { get; init; }
}

/// <summary>联合选择容器各网格的候选，以逐网格合法布局为保底。</summary>
public sealed class ContainerPacker
{
    private readonly IPacker _single;

    public ContainerPacker(IPacker single) => _single = single;

    public ContainerPackResult Pack(IReadOnlyList<PackRequest> requests, double seconds)
    {
        if (requests.Count == 1)
        {
            PackResult result = _single.Pack(requests[0], seconds);
            return new ContainerPackResult(new[] { result })
            {
                Diagnostic = result.Diagnostic,
                Warning = result.Warning,
            };
        }
        var elapsed = Stopwatch.StartNew();
        var used = new HashSet<string>();
        var grids = new List<PackResult>();
        foreach (PackRequest request in requests)
        {
            PackRequest rest = request with
            {
                Items = request.Items.Where(i => !used.Contains(i.Id)).ToArray(),
            };
            PackResult result = _single.Pack(rest, 0);
            var placed = new HashSet<string>(result.Placements.Select(p => p.Id));
            grids.Add(result with
            {
                Unplaced = request.Items.Where(i => !placed.Contains(i.Id)).ToArray(),
            });
            used.UnionWith(result.Placements.Select(p => p.Id));
        }
        var baseline = new ContainerPackResult(grids);
        double remaining = seconds - elapsed.Elapsed.TotalSeconds;
        if ((_single is not CpSatPacker && _single is not CachedPacker)
            || remaining <= 0)
        {
            return baseline with { Diagnostic = "container skip=budget-or-heuristic" };
        }
        try
        {
            string status;
            ContainerPackResult? solved = CategoryPacking.HasCategories(requests)
                ? CategoryPackingModel.Solve(requests, baseline, remaining, out status)
                : MultiGridModel.Solve(requests, baseline, remaining, out status);
            ContainerPackResult best = solved != null
                && Better(requests, solved, baseline)
                ? solved : baseline;
            return best with
            {
                Diagnostic = $"multi-grid cp-sat {status}, " +
                    $"{elapsed.ElapsedMilliseconds} ms, grids={requests.Count}, " +
                    $"area {Area(requests, baseline)}->{Area(requests, best)}, " +
                    $"movable-rows-sum {Height(requests, baseline)}" +
                    $"->{Height(requests, best)}, " +
                    $"category-span {Span(requests, baseline)}->{Span(requests, best)}",
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException
            || ex is TypeInitializationException
            || ex is System.IO.FileNotFoundException
            || ex is System.IO.FileLoadException)
        {
            return baseline with
            {
                Diagnostic = $"multi-grid solver unavailable: {ex}",
                Warning = "联合求解器加载失败，已采用保底布局",
            };
        }
    }

    private static bool Better(IReadOnlyList<PackRequest> requests,
        ContainerPackResult next, ContainerPackResult before)
    {
        if (next.Grids.Any(g => g.Unplaced.Any(i => i.Required)))
        {
            return false;
        }
        int difference = Area(requests, next) - Area(requests, before);
        return difference > 0 || (difference == 0
            && (Height(requests, next) < Height(requests, before)
                || (Height(requests, next) == Height(requests, before)
                    && CategoryPacking.Compare(requests, next, before) < 0)));
    }

    private static string Span(
        IReadOnlyList<PackRequest> requests, ContainerPackResult result) =>
        "[" + string.Join(",", Enumerable.Range(0, requests.Max(CategoryPacking.Depth))
            .Select(d => requests.Select((r, i) =>
                CategoryPacking.Span(r, result.Grids[i], d)).Sum())) + "]";

    internal static int Area(
        IReadOnlyList<PackRequest> requests, ContainerPackResult result) =>
        requests.Select((r, i) => CpSatPacker.Area(r, result.Grids[i])).Sum();

    internal static int Height(
        IReadOnlyList<PackRequest> requests, ContainerPackResult result) =>
        requests.Select((r, i) => CpSatPacker.Height(r, result.Grids[i])).Sum();
}
