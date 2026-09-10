using System.Collections.Generic;
using System.Threading.Tasks;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Packing;

namespace ChouUn.InventoryOrganizer.Core.Organizing;

/// <summary>
/// 编排层对游戏的出口。每个操作由实现方先模拟再提交，失败时返回原因。
/// </summary>
public interface IInventoryPort
{
    /// <summary>
    /// 读取根容器的当前快照。每个阶段开始前调用，保证尺寸与位置是最新的。
    /// </summary>
    ItemSnapshot ReadSnapshot();

    Task<PortResult> FoldAsync(string itemId);

    /// <summary>
    /// 把物品移入目标容器中第一个接受它且有空位的网格；没有这样的网格时失败。
    /// </summary>
    Task<PortResult> MoveAsync(string itemId, string containerId);

    /// <summary>读取容器各网格直属的可堆叠物品，不读取子容器或弹匣内部。</summary>
    IReadOnlyList<StackSnapshot> ReadStacks(string containerId);

    /// <summary>查询游戏的同类物品判定，包括当前生效的合并兼容规则。</summary>
    bool CanStack(string sourceId, string targetId);

    /// <summary>先模拟再提交合并或部分补充，返回实际数量，失败不视为已转移。</summary>
    Task<StackMergeResult> MergeAsync(string sourceId, string targetId);

    /// <summary>
    /// 把网格里给定的 Free 物品挪到新位置，其余物品原地不动。
    /// 任一位置放不下则整个网格保持原样并失败。
    /// </summary>
    Task<PortResult> ArrangeAsync(
        string containerId, int gridIndex, IReadOnlyList<Placement> placements);
}

public sealed record StackSnapshot(
    string Id, string TemplateId, int Count, int Capacity, LockState Lock);

public sealed record StackMergeResult(
    int Transferred, int SourceRemaining, int TargetCount, string? Error);

public sealed record PortResult(bool Succeeded, string? Error)
{
    public static PortResult Ok { get; } = new PortResult(true, null);

    public static PortResult Fail(string error) => new PortResult(false, error);
}
