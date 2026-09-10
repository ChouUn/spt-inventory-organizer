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
            report.StackReads++;
            foreach (StackSnapshot stack in _port.ReadStacks(container.Id))
                Update(stack, container.Id);
        }
        // 全树只建立一次未满堆索引；先完成同箱合并，再按模板跨层配对。
        foreach (HashSet<string> ids in _targets.Values.ToArray())
        {
            await ExecuteAsync(Plan(ids, false, root.Id, report), root.Id, report);
        }
        foreach (HashSet<string> ids in _templates.Values.ToArray())
        {
            await ExecuteAsync(Plan(ids, true, root.Id, report), root.Id, report);
        }
    }

    /// <summary>仅对未满堆推演，填满目标或耗尽来源后立即移出待配对集合。</summary>
    private IReadOnlyList<(string Source, string Target)> Plan(
        IEnumerable<string> ids, bool cross, string rootId, OrganizeReport report)
    {
        var pending = new LinkedList<StackSnapshot>(ids.Select(id => _stacks[id])
            .OrderByDescending(s => s.Lock == LockState.Pinned)
            // 可转移的根堆优先补子容器；数量不能反转归属方向。
            .ThenBy(s => _owners[s.Id] == rootId)
            .ThenByDescending(s => s.Count)
            .ThenBy(s => s.Id, StringComparer.Ordinal));
        var plan = new List<(string Source, string Target)>();
        var useful = new HashSet<string>();
        while (pending.Last != null)
        {
            StackSnapshot source = pending.Last.Value;
            pending.RemoveLast();
            if (source.Lock != LockState.Free) { continue; }
            LinkedListNode<StackSnapshot>? node = pending.First;
            while (source.Count > 0 && node != null)
            {
                StackSnapshot target = node.Value;
                LinkedListNode<StackSnapshot>? next = node.Next;
                if ((!cross || _owners[source.Id] != _owners[target.Id])
                    && CanStack(source.Id, target.Id, report))
                {
                    int amount = Math.Min(source.Count, target.Capacity - target.Count);
                    source = source with { Count = source.Count - amount };
                    target = target with { Count = target.Count + amount };
                    plan.Add((source.Id, target.Id));
                    if (source.Count == 0 || target.Lock == LockState.Pinned)
                    {
                        useful.Add(source.Id);
                        useful.Add(target.Id);
                    }
                    if (target.Count == target.Capacity) { pending.Remove(node); }
                    else { node.Value = target; }
                }
                node = next;
            }
        }
        return cross ? UsefulTransfers(plan, useful) : plan;
    }

    /// <summary>按连接关系保留能释放占位或补充 Pinned 的整条转移链。</summary>
    private static IReadOnlyList<(string Source, string Target)> UsefulTransfers(
        List<(string Source, string Target)> plan, HashSet<string> useful)
    {
        var neighbors = new Dictionary<string, List<string>>();
        foreach ((string source, string target) in plan)
        {
            if (!neighbors.ContainsKey(source))
                neighbors[source] = new List<string>();
            if (!neighbors.ContainsKey(target))
                neighbors[target] = new List<string>();
            neighbors[source].Add(target);
            neighbors[target].Add(source);
        }
        var pending = new Queue<string>(useful);
        while (pending.Count > 0)
        {
            foreach (string id in neighbors[pending.Dequeue()])
                if (useful.Add(id)) { pending.Enqueue(id); }
        }
        return plan.Where(p => useful.Contains(p.Source)).ToArray();
    }

    private async Task ExecuteAsync(IReadOnlyList<(string Source, string Target)> plan,
        string rootId, OrganizeReport report)
    {
        foreach ((string source, string target) in plan)
        {
            if (!_owners.TryGetValue(source, out string? owner)
                || !_owners.TryGetValue(target, out string? destination)) { continue; }
            StackMergeResult? result = await TryMergeAsync(source, target, report);
            if (result?.SourceRemaining == 0
                && owner == rootId && destination != rootId)
            {
                report.Moved++;
            }
        }
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
        foreach (StackSnapshot target in Ordered(targets.Select(id => _stacks[id]))
            .ToArray())
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
        string sourceOwner = _owners[source.Id];
        string targetOwner = _owners[target.Id];
        var stopwatch = Stopwatch.StartNew();
        StackMergeResult result = await _port.MergeAsync(source.Id, target.Id);
        report.MergeMilliseconds += stopwatch.ElapsedMilliseconds;
        if (result.Error != null)
        {
            report.Failures.Add($"合并 {source.Id} -> {target.Id}：{result.Error}");
            Reload(sourceOwner, report);
            if (sourceOwner != targetOwner) { Reload(targetOwner, report); }
            return null;
        }
        Update(target with { Count = result.TargetCount }, targetOwner);
        Update(source with { Count = result.SourceRemaining }, sourceOwner);
        if (result.SourceRemaining == 0) { Consumed.Add(source.Id); }
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
            Update(stack, containerId);
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
            Remove(id);
        }
        report.StackReads++;
        foreach (StackSnapshot stack in _port.ReadStacks(containerId))
            Update(stack, containerId);
    }

    /// <summary>所有索引只保留未满且未锁的堆叠，事务结果同步增删。</summary>
    private void Update(StackSnapshot stack, string containerId)
    {
        Remove(stack.Id);
        if (stack.Count <= 0 || stack.Count >= stack.Capacity
            || stack.Lock == LockState.Locked) { return; }
        _stacks.Add(stack.Id, stack);
        _owners.Add(stack.Id, containerId);
        if (!_templates.TryGetValue(stack.TemplateId, out HashSet<string>? ids))
            _templates.Add(stack.TemplateId, ids = new HashSet<string>());
        ids.Add(stack.Id);
        var key = (containerId, stack.TemplateId);
        if (!_targets.TryGetValue(key, out HashSet<string>? targets))
            _targets.Add(key, targets = new HashSet<string>());
        targets.Add(stack.Id);
    }

    private void Remove(string id)
    {
        if (!_stacks.TryGetValue(id, out StackSnapshot? stack)) { return; }
        _templates[stack.TemplateId].Remove(id);
        _targets[(_owners[id], stack.TemplateId)].Remove(id);
        _owners.Remove(id);
        _stacks.Remove(id);
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
