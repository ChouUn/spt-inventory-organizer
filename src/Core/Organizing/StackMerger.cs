using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ChouUn.InventoryOrganizer.Core.Inventory;

namespace ChouUn.InventoryOrganizer.Core.Organizing;

/// <summary>压实容器内堆叠，并在按规则收纳时补充目标堆叠；游戏决定能否合并。</summary>
internal sealed class StackMerger
{
    private readonly IInventoryPort _port;

    public StackMerger(IInventoryPort port) => _port = port;

    public async Task MergeContainersAsync(ItemSnapshot root, OrganizeReport report)
    {
        foreach (ItemSnapshot container in OrganizeScope.Containers(root))
        {
            StackSnapshot[] stacks = Ordered(_port.ReadStacks(container.Id)).ToArray();
            for (int i = 0; i < stacks.Length; i++)
            {
                // 只向前补充：Pinned 优先，其余大堆优先；从后方小堆取出以释放格子。
                // 不反向补充，避免剩余的小堆再次抽空刚补满的堆叠。
                for (int j = stacks.Length - 1;
                     j > i && stacks[i].Count > 0
                         && stacks[i].Count < stacks[i].Capacity;
                     j--)
                {
                    StackMergeResult? result = await TryMergeAsync(
                        stacks[j], stacks[i], report);
                    if (result != null)
                    {
                        stacks[j] = stacks[j] with { Count = result.SourceRemaining };
                        stacks[i] = stacks[i] with { Count = result.TargetCount };
                    }
                }
            }
        }
    }

    /// <summary>
    /// 匹配 tag 后才调用，返回来源是否已全部合入目标容器。
    /// 每次读取直属堆叠，纳入前一条规则的部分补充和刚移动进来的新堆叠。
    /// </summary>
    public async Task<bool> TopUpAsync(
        string rootId, string sourceId, string containerId, OrganizeReport report)
    {
        StackSnapshot? source = _port.ReadStacks(rootId)
            .FirstOrDefault(s => s.Id == sourceId);
        if (source is null)
        {
            return false;
        }
        foreach (StackSnapshot target in Ordered(_port.ReadStacks(containerId)))
        {
            StackMergeResult? result = await TryMergeAsync(source, target, report);
            if (result is null)
            {
                continue;
            }
            if (result.SourceRemaining == 0)
            {
                return true;
            }
            source = source with { Count = result.SourceRemaining };
        }
        return false;
    }

    private async Task<StackMergeResult?> TryMergeAsync(
        StackSnapshot source, StackSnapshot target, OrganizeReport report)
    {
        if (source.Lock != LockState.Free || source.Count <= 0
            || target.Lock == LockState.Locked || target.Count >= target.Capacity
            || source.TemplateId != target.TemplateId || source.Id == target.Id
            || !_port.CanStack(source.Id, target.Id))
        {
            return null;
        }
        var stopwatch = Stopwatch.StartNew();
        StackMergeResult result = await _port.MergeAsync(source.Id, target.Id);
        report.MergeMilliseconds += stopwatch.ElapsedMilliseconds;
        if (result.Error != null)
        {
            report.Failures.Add($"合并 {source.Id} -> {target.Id}：{result.Error}");
            return null;
        }
        if (result.Transferred > 0)
        {
            report.Merged++;
        }
        return result;
    }

    private static IEnumerable<StackSnapshot> Ordered(
        IReadOnlyList<StackSnapshot> stacks)
    {
        return stacks.Where(s => s.Lock != LockState.Locked && s.Count > 0)
            .OrderByDescending(s => s.Lock == LockState.Pinned)
            .ThenByDescending(s => s.Count)
            .ThenBy(s => s.Id, StringComparer.Ordinal);
    }
}
