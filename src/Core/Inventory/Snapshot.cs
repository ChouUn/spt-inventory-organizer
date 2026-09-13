using System.Collections.Generic;

namespace ChouUn.StashMaster.Core.Inventory;

/// <summary>游戏内置锁，与游戏的 Free / Pinned / Locked 三态一一对应。</summary>
public enum LockState
{
    Free,
    Pinned,
    Locked,
}

/// <summary>物品在所在网格中的位置。<see cref="Rotated"/> 为真表示已转置。</summary>
public sealed record GridPosition(int X, int Y, bool Rotated);

/// <summary>容器里的一个网格及其中的物品。</summary>
public sealed record GridSnapshot(
    int Index,
    int Width,
    int Height,
    IReadOnlyList<ItemSnapshot> Items);

/// <summary>
/// 一个物品的纯数据描述。<see cref="Width"/> 与 <see cref="Height"/> 是当前折叠状态下、
/// 未转置的格子数。<see cref="Position"/> 为 null 表示不在网格里，例如整理的根容器。
/// 有网格的物品就是容器，<see cref="Grids"/> 非空。
/// </summary>
public sealed record ItemSnapshot(
    string Id,
    string TemplateId,
    string Name,
    string ShortName,
    IReadOnlyList<string> Categories,
    int Width,
    int Height,
    GridPosition? Position,
    bool CanFold,
    bool Folded,
    LockState Lock,
    string? Tag,
    IReadOnlyList<GridSnapshot> Grids)
{
    /// <summary>用于自定义排序的物品类型名；未知时为空。</summary>
    public string SortType { get; init; } = "";

    /// <summary>SortType 边界以内的类别 ID 链；不含边界本身及旧祖先。</summary>
    public IReadOnlyList<string> SubcategoryPath { get; init; } =
        System.Array.Empty<string>();
}
