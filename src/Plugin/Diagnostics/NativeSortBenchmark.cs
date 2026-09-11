#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ChouUn.StashMaster.Core.Organizing;
using ChouUn.StashMaster.Core.Packing;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ChouUn.StashMaster.Diagnostics;

/// <summary>Debug 专用：同一输入上测量原生列表排序与网格落位，不应用结果。</summary>
internal sealed class NativeSortBenchmark
{
    private static bool _ready;
    private readonly CompoundItem _root;
    internal double Milliseconds { get; private set; }

    internal NativeSortBenchmark(CompoundItem root) => _root = root;

    internal void Compare(IReadOnlyList<GridPackJob> jobs,
        ContainerPackResult ours, double planningMs)
    {
        var total = Stopwatch.StartNew();
        try
        {
            Prepare();
            Dictionary<string, Item> items = _root.GetAllItems()
                .ToDictionary(i => i.Id);
            var timings = new List<double>();
            var results = new List<PackResult>();
            foreach (GridPackJob job in jobs)
            {
                var watch = Stopwatch.StartNew();
                PackResult result = SortSimulation.Run(
                    job, items, Sort, NativeGridSearch.Find);
                watch.Stop();
                timings.Add(watch.Elapsed.TotalMilliseconds);
                results.Add(result);
            }
            Plugin.Log.LogInfo(
                $"sort-benchmark: scope=final-per-grid, applied=false, " +
                $"organizer-plan={planningMs:F2}ms, " +
                $"native-list+placement={timings.Sum():F2}ms; " +
                "excludes inventory checks, transactions and UI");
            for (int i = 0; i < jobs.Count; i++)
            {
                GridPackJob job = jobs[i];
                Plugin.Log.LogInfo(
                    $"sort-benchmark {job.Container.Name}/{job.GridIndex}: " +
                    $"items={job.FreeItems.Count}, native={timings[i]:F2}ms, " +
                    $"organizer[{Describe(job.Request, ours.Grids[i])}], " +
                    $"native[{Describe(job.Request, results[i])}]");
            }
        }
        catch (Exception ex)
        {
            // 基准失败不影响已得到的正式布局，也不把部分基准结果称为成功。
            Plugin.Log.LogWarning($"sort-benchmark unavailable: {ex}");
        }
        finally
        {
            Milliseconds += total.Elapsed.TotalMilliseconds;
        }
    }

    private static string Describe(PackRequest request, PackResult result)
    {
        var sizes = request.Items.ToDictionary(i => i.Id);
        int rows = result.Placements.Select(p => p.Y + (p.Rotated
            ? sizes[p.Id].Width : sizes[p.Id].Height)).DefaultIfEmpty().Max();
        string spans = string.Join(",", CategoryPacking.Spans(request, result));
        return $"complete={result.Complete}, rows={rows}, span=[{spans}]";
    }

    /// <summary>只复制原方法体，保留其他 mod 的所有补丁与配置。</summary>
    private static void Prepare()
    {
        if (_ready) { return; }
        // 原方法体中的下游调用仍可能被 patch；遇到这种情况不冒称纯原生。
        foreach (MethodInfo method in new[]
        {
            AccessTools.Method(typeof(Grid), nameof(Grid.SetLayout),
                new[] { typeof(IntVec2), typeof(LocationInGrid),
                    typeof(bool), typeof(bool) }),
            AccessTools.Method(typeof(Grid), nameof(Grid.FillSpaceBuffer)),
        })
        {
            if (Harmony.GetPatchInfo(method)?.Owners.Count > 0)
            {
                throw new InvalidOperationException(
                    $"native dependency patched: {method.Name}");
            }
        }
        Reverse(typeof(ItemSorter), nameof(ItemSorter.Sort), nameof(Sort));
        NativeGridSearch.Prepare();
        _ready = true;
    }

    private static void Reverse(Type type, string method, string standin)
    {
        Harmony.ReversePatch(AccessTools.Method(type, method),
            new HarmonyMethod(AccessTools.Method(
                typeof(NativeSortBenchmark), standin)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<Item> Sort(IEnumerable<Item> items) =>
        throw new NotSupportedException("reverse patch not initialized");
}
#endif
