using System;
using System.Collections.Generic;
using System.Linq;

namespace ChouUn.InventoryOrganizer.Core.Packing;

/// <summary>复用相同输入的限时结果；输入变化或获得更长预算时重新求解。</summary>
public sealed class CachedPacker : IPacker
{
    private readonly IPacker _inner;
    private readonly List<Entry> _entries = new();
    private FinalEntry? _final;

    public CachedPacker(IPacker inner) => _inner = inner;

    /// <summary>全部最终网格共同缓存，输入和输出位置均匹配时不重复联合求解。</summary>
    public ContainerPackResult PackAll(IReadOnlyList<PackRequest> requests,
        double seconds)
    {
        PackRequest[] keys = requests.Select(Normalize).ToArray();
        if (_final != null && _final.Input.Length == keys.Length
            && _final.Seconds >= seconds && keys.Select((key, i) =>
                Same(key, _final.Input[i]) || Same(key, _final.Output[i])).All(x => x))
        {
            return _final.Result with { Diagnostic = "global skip=unchanged-input" };
        }
        ContainerPackResult result = new GlobalPacker(_inner).Pack(requests, seconds);
        if (result.Warning == null && result.Grids.All(g => g.Complete))
        {
            _final = new FinalEntry(keys, keys.Select((key, i) => key with
            {
                Current = result.Grids[i].Placements
                    .OrderBy(p => p.Id, StringComparer.Ordinal).ToArray(),
            }).ToArray(), result, seconds);
        }
        return result;
    }

    public PackResult Pack(PackRequest request, double maxSeconds = 1)
    {
        if (maxSeconds <= 0) { return _inner.Pack(request, maxSeconds); }
        PackRequest key = Normalize(request);
        Entry? cached = _entries.FirstOrDefault(e =>
            e.Seconds >= maxSeconds && Same(e.Request, key));
        if (cached != null)
        {
            return cached.Result with
            {
                Diagnostic = "skip=unchanged-input; previous "
                    + cached.Result.Diagnostic,
            };
        }
        PackResult result = _inner.Pack(request, maxSeconds);
        if (result.Warning == null && !result.Unplaced.Any(i => i.Required))
        {
            Add(key, result, maxSeconds);
            // 排布成功后的快照也对应同一个问题；失败时实际坐标不同，不会误命中。
            if (result.Complete && request.Items.All(i => i.Required))
            {
                Add(key with
                {
                    Current = result.Placements.OrderBy(p => p.Id,
                        StringComparer.Ordinal).ToArray(),
                }, result, maxSeconds);
            }
        }
        return result;
    }

    private void Add(PackRequest request, PackResult result, double seconds)
    {
        _entries.RemoveAll(e => Same(e.Request, request));
        _entries.Add(new Entry(request, result, seconds));
        if (_entries.Count > 32) { _entries.RemoveAt(0); }
    }

    private static PackRequest Normalize(PackRequest request) => request with
    {
        Items = request.Items.OrderBy(i => i.Id, StringComparer.Ordinal).ToArray(),
        Current = request.Current.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray(),
        Fixed = request.Fixed.OrderBy(b => b.Y).ThenBy(b => b.X)
            .ThenBy(b => b.Width).ThenBy(b => b.Height).ToArray(),
    };

    private static bool Same(PackRequest a, PackRequest b) =>
        a.Width == b.Width && a.Height == b.Height && a.Fixed.SequenceEqual(b.Fixed)
        && a.Items.Count == b.Items.Count
        && a.Items.Zip(b.Items, (x, y) => x.Id == y.Id
            && x.TemplateId == y.TemplateId && x.Width == y.Width
            && x.Height == y.Height && x.Required == y.Required
            && x.CategoryPath.SequenceEqual(y.CategoryPath)).All(equal => equal)
        && a.Current.SequenceEqual(b.Current);

    private sealed record Entry(PackRequest Request, PackResult Result, double Seconds);
    private sealed record FinalEntry(PackRequest[] Input, PackRequest[] Output,
        ContainerPackResult Result, double Seconds);
}
