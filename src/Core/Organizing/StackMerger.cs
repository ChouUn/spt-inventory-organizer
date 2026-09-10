using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ChouUn.InventoryOrganizer.Core.Inventory;

namespace ChouUn.InventoryOrganizer.Core.Organizing;

/// <summary>统一堆叠与收纳补充共用索引，游戏决定兼容性和实际转移量。</summary>
internal sealed class StackMerger
{
    private readonly IInventoryPort _port;
    private readonly Dictionary<string, StackSnapshot> _stacks = new();
    private readonly Dictionary<string, string> _owners = new();
    private readonly Dictionary<string, HashSet<string>> _templates = new();
    private readonly Dictionary<(string Container, string Template), HashSet<string>>
        _targets = new();
    public HashSet<string> Consumed { get; } = new();

    public StackMerger(IInventoryPort port) => _port = port;

    public async Task MergeContainersAsync(ItemSnapshot root, OrganizeReport report,
        Func<string, Tags.TagParseResult>? parse = null)
    {
        ItemSnapshot[] containers = OrganizeScope.Containers(root, parse).ToArray();
        _stacks.Clear();
        _owners.Clear();
        _templates.Clear();
        _targets.Clear();
        Consumed.Clear();
        foreach (ItemSnapshot container in containers)
        {
            Reload(container.Id, report);
        }
        // 同箱能完成的合并先做，避免等价空间收益下跨箱转移库存。
        foreach (IGrouping<string, StackSnapshot> group in _stacks.Values
            .GroupBy(s => s.TemplateId).ToArray())
        {
            foreach (IGrouping<string, StackSnapshot> local in group
                .GroupBy(s => _owners[s.Id]))
            {
                string[] ids = Ordered(local).Select(s => s.Id).ToArray();
                for (int i = 0; i < ids.Length; i++)
                {
                    for (int j = ids.Length - 1; j > i; j--)
                    {
                        if (!_stacks.TryGetValue(ids[i], out StackSnapshot? target)
                            || target.Count >= target.Capacity)
                        {
                            break;
                        }
                        await TryMergeAsync(ids[j], ids[i], report);
                    }
                }
            }
        }
        foreach (string template in _templates.Keys.ToArray())
        {
            foreach ((string source, string target) in PlanCross(
                template, root.Id, report))
            {
                string owner = _owners[source];
                StackMergeResult? result = await TryMergeAsync(source, target, report);
                if (result?.SourceRemaining == 0 && owner == root.Id
                    && _owners[target] != root.Id)
                {
                    report.Moved++;
                }
            }
        }
    }

    /// <summary>
    /// 先在数量副本上推演跨箱转移。相连的转移链必须释放堆叠或补充 Pinned，
    /// 才值得执行；例如三堆 40/60 需要两次转移才能释放一格。
    /// </summary>
    private IReadOnlyList<(string Source, string Target)> PlanCross(
        string template, string rootId, OrganizeReport report)
    {
        StackSnapshot[] ordered = _templates[template].Select(id => _stacks[id])
            .Where(s => s.Lock != LockState.Locked && s.Count > 0)
            .OrderByDescending(s => s.Lock == LockState.Pinned)
            .ThenByDescending(s => s.Count)
            .ThenBy(s => _owners[s.Id] == rootId)
            .ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var counts = ordered.ToDictionary(s => s.Id, s => s.Count);
        var plan = new List<(string Source, string Target)>();
        var useful = new HashSet<string>();
        for (int i = 0; i < ordered.Length; i++)
        {
            StackSnapshot target = ordered[i];
            if (counts[target.Id] == 0 || counts[target.Id] >= target.Capacity)
            {
                continue;
            }
            for (int j = ordered.Length - 1;
                j > i && counts[target.Id] < target.Capacity; j--)
            {
                StackSnapshot source = ordered[j];
                if (source.Lock != LockState.Free || counts[source.Id] == 0
                    || _owners[source.Id] == _owners[target.Id]
                    || !CanStack(source.Id, target.Id, report))
                {
                    continue;
                }
                int amount = Math.Min(counts[source.Id],
                    target.Capacity - counts[target.Id]);
                counts[source.Id] -= amount;
                counts[target.Id] += amount;
                plan.Add((source.Id, target.Id));
                if (counts[source.Id] == 0 || target.Lock == LockState.Pinned)
                {
                    useful.Add(source.Id);
                    useful.Add(target.Id);
                }
            }
        }
        bool changed;
        do
        {
            changed = false;
            foreach ((string source, string target) in plan)
            {
                if (useful.Contains(source) || useful.Contains(target))
                {
                    changed |= useful.Add(source);
                    changed |= useful.Add(target);
                }
            }
        } while (changed);
        return plan.Where(p => useful.Contains(p.Source)).ToArray();
    }

    /// <summary>
    /// 匹配 tag 后才调用，返回来源是否已全部合入目标容器。
    /// 索引按事务结果更新，纳入部分补充和刚移入的新堆叠。
    /// </summary>
    public async Task<bool> TopUpAsync(
        string sourceId, string containerId, OrganizeReport report)
    {
        if (!_stacks.TryGetValue(sourceId, out StackSnapshot? source))
        {
            return false;
        }
        if (!_targets.TryGetValue((containerId, source.TemplateId),
            out HashSet<string>? targets))
        {
            return false;
        }
        foreach (StackSnapshot target in Ordered(targets.Select(id => _stacks[id])
            .Where(s => s.Count < s.Capacity)).ToArray())
        {
            StackMergeResult? result = await TryMergeAsync(sourceId, target.Id, report);
            if (result is null)
            {
                continue;
            }
            if (result.SourceRemaining == 0)
            {
                return true;
            }
        }
        return false;
    }

    private async Task<StackMergeResult?> TryMergeAsync(
        string sourceId, string targetId, OrganizeReport report)
    {
        if (!_stacks.TryGetValue(sourceId, out StackSnapshot? source)
            || !_stacks.TryGetValue(targetId, out StackSnapshot? target)
            || source.Lock != LockState.Free || source.Count <= 0
            || target.Lock == LockState.Locked || target.Count >= target.Capacity
            || source.TemplateId != target.TemplateId || source.Id == target.Id
            || !CanStack(source.Id, target.Id, report))
        {
            return null;
        }
        var stopwatch = Stopwatch.StartNew();
        StackMergeResult result = await _port.MergeAsync(source.Id, target.Id);
        report.MergeMilliseconds += stopwatch.ElapsedMilliseconds;
        if (result.Error != null)
        {
            report.Failures.Add($"合并 {source.Id} -> {target.Id}：{result.Error}");
            Reload(_owners[source.Id], report);
            if (_owners[source.Id] != _owners[target.Id])
            {
                Reload(_owners[target.Id], report);
            }
            return null;
        }
        _stacks[target.Id] = target with { Count = result.TargetCount };
        if (result.SourceRemaining == 0)
        {
            _stacks.Remove(source.Id);
            _templates[source.TemplateId].Remove(source.Id);
            _targets[(_owners[source.Id], source.TemplateId)].Remove(source.Id);
            Consumed.Add(source.Id);
        }
        else
        {
            _stacks[source.Id] = source with { Count = result.SourceRemaining };
        }
        if (result.Transferred > 0)
        {
            report.Merged++;
        }
        return result;
    }

    public void Moved(string itemId, string containerId)
    {
        if (_stacks.TryGetValue(itemId, out StackSnapshot? stack))
        {
            _targets[(_owners[itemId], stack.TemplateId)].Remove(itemId);
            _owners[itemId] = containerId;
            AddTarget(containerId, stack);
        }
    }

    private bool CanStack(string source, string target, OrganizeReport report)
    {
        report.StackChecks++;
        return _port.CanStack(source, target);
    }

    /// <summary>初始化或失败后校准单个容器，正常事务不重复扫描游戏物品。</summary>
    private void Reload(string containerId, OrganizeReport report)
    {
        foreach (string id in _stacks.Keys.Where(id => _owners[id] == containerId)
            .ToArray())
        {
            _templates[_stacks[id].TemplateId].Remove(id);
            _targets[(containerId, _stacks[id].TemplateId)].Remove(id);
            _stacks.Remove(id);
        }
        report.StackReads++;
        foreach (StackSnapshot stack in _port.ReadStacks(containerId))
        {
            _stacks[stack.Id] = stack;
            _owners[stack.Id] = containerId;
            if (!_templates.TryGetValue(stack.TemplateId, out HashSet<string>? ids))
            {
                _templates.Add(stack.TemplateId, ids = new HashSet<string>());
            }
            ids.Add(stack.Id);
            AddTarget(containerId, stack);
        }
    }

    private void AddTarget(string containerId, StackSnapshot stack)
    {
        var key = (containerId, stack.TemplateId);
        if (!_targets.TryGetValue(key, out HashSet<string>? targets))
        {
            _targets.Add(key, targets = new HashSet<string>());
        }
        targets.Add(stack.Id);
    }

    private static IEnumerable<StackSnapshot> Ordered(
        IEnumerable<StackSnapshot> stacks)
    {
        return stacks.Where(s => s.Lock != LockState.Locked && s.Count > 0)
            .OrderByDescending(s => s.Lock == LockState.Pinned)
            .ThenByDescending(s => s.Count)
            .ThenBy(s => s.Id, StringComparer.Ordinal);
    }
}
