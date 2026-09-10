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
        var elapsed = Stopwatch.StartNew();
        var identity = new PackingIdentity(requests);
        return identity.Restore(PackAnonymous(identity.Requests,
            System.Math.Max(0, seconds - elapsed.Elapsed.TotalSeconds)));
    }

    private ContainerPackResult PackAnonymous(
        IReadOnlyList<PackRequest> requests, double seconds)
    {
        var elapsed = Stopwatch.StartNew();
        var used = new HashSet<string>();
        var grids = new List<PackResult>();
        foreach (PackRequest request in requests)
        {
            PackRequest rest = request with
            {
                Items = request.Items.Where(i => !used.Contains(i.Id)).ToArray(),
            };
            PackResult result = Baseline(rest);
            var placed = new HashSet<string>(result.Placements.Select(p => p.Id));
            grids.Add(result with
            {
                Unplaced = request.Items.Where(i => !placed.Contains(i.Id)).ToArray(),
            });
            used.UnionWith(result.Placements.Select(p => p.Id));
        }
        var baseline = new ContainerPackResult(grids);
        int area = Area(requests, baseline);
        long upper = AreaUpperBound(requests);
        if (!grids.Any(g => g.Unplaced.Any(i => i.Required)) && area == upper)
        {
            return baseline with
            {
                Diagnostic = $"collection skip=area-bound; objective=area; "
                    + $"area={area}, upper={upper}, grids={requests.Count}",
            };
        }
        double remaining = seconds - elapsed.Elapsed.TotalSeconds;
        if ((_single is not CpSatPacker && _single is not CachedPacker)
            || remaining <= 0)
        {
            return baseline with { Diagnostic = "container skip=budget-or-heuristic" };
        }
        try
        {
            ContainerPackResult? solved = MultiGridModel.Solve(
                requests, baseline, remaining, out string status);
            ContainerPackResult best = solved != null
                && Better(requests, solved, baseline)
                ? solved : baseline;
            return best with
            {
                Diagnostic = $"collection cp-sat {status}; objective=area; "
                    + $"{elapsed.ElapsedMilliseconds} ms, grids={requests.Count}, "
                    + $"area {area}->{Area(requests, best)}, upper={upper}",
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
        return before.Grids.Any(g => g.Unplaced.Any(i => i.Required))
            || Area(requests, next) > Area(requests, before);
    }

    /// <summary>先原位填空；只有能多收物品时才采用重排，不比较高度或类别。</summary>
    private PackResult Baseline(PackRequest request)
    {
        IPacker heuristic = _single is CpSatPacker || _single is CachedPacker
            ? new HeuristicPacker() : _single;
        var current = new HashSet<string>(request.Current.Select(p => p.Id));
        PackResult fill = heuristic.Pack(request with
        {
            Fixed = request.Fixed.Concat(CpSatPacker.Blocks(request, request.Current))
                .ToArray(),
            Items = request.Items.Where(i => !current.Contains(i.Id)).ToArray(),
        }, 0);
        var preserved = new PackResult(request.Current.Concat(fill.Placements)
            .ToArray(), fill.Unplaced);
        if (!preserved.Unplaced.Any(i => i.Required)
            && CpSatPacker.Area(request, preserved)
                == AreaUpperBound(new[] { request }))
        {
            return preserved;
        }
        PackResult fresh = heuristic.Pack(request, 0);
        return !fresh.Unplaced.Any(i => i.Required)
            && (preserved.Unplaced.Any(i => i.Required)
                || CpSatPacker.Area(request, fresh)
                    > CpSatPacker.Area(request, preserved))
            ? fresh : preserved;
    }

    /// <summary>候选按身份去重，容量扣除固定障碍；上界不使用高度或类别。</summary>
    internal static long AreaUpperBound(IReadOnlyList<PackRequest> requests) =>
        Math.Min(requests.SelectMany(r => r.Items).GroupBy(i => i.Id)
            .Sum(g => (long)g.First().Width * g.First().Height),
            requests.Sum(r => Math.Min(CpSatPacker.AvailableCells(r)[r.Height],
                r.Items.Sum(i => (long)i.Width * i.Height))));

    internal static int Area(
        IReadOnlyList<PackRequest> requests, ContainerPackResult result) =>
        requests.Select((r, i) => CpSatPacker.Area(r, result.Grids[i])).Sum();

    internal static int Height(
        IReadOnlyList<PackRequest> requests, ContainerPackResult result) =>
        requests.Select((r, i) => CpSatPacker.Height(r, result.Grids[i])).Sum();
}
