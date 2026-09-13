using System.Collections.Generic;

namespace ChouUn.StashMaster.Core.Packing;

/// <summary>待排布的物品，尺寸为未转置的格子数。</summary>
public sealed record PackItem(string Id, string TemplateId, int Width, int Height)
{
    /// <summary>目标容器原有物品必须保留；新收纳候选可以不选中。</summary>
    public bool Required { get; init; }

    /// <summary>统一排序与聚合树的顶层类型名；空值归入未识别类型组。</summary>
    public string SortType { get; init; } = "";

    /// <summary>SortType 内部子类别的 ID 链；不含类型边界及外部祖先，空链直接归类型根。</summary>
    public IReadOnlyList<string> SubcategoryPath { get; init; } =
        System.Array.Empty<string>();
}

/// <summary>网格里已被固定物品占住的矩形，尺寸已按其当前转置展开。</summary>
public sealed record FixedBlock(int X, int Y, int Width, int Height);

public sealed record Placement(string Id, int X, int Y, bool Rotated);

public sealed record PackRequest(
    int Width,
    int Height,
    IReadOnlyList<FixedBlock> Fixed,
    IReadOnlyList<PackItem> Items)
{
    /// <summary>统一树顶层从上到下的顺序；空列表只关闭顺序约束，不改变聚合树。</summary>
    public IReadOnlyList<string> CategoryOrder { get; init; } =
        System.Array.Empty<string>();

    /// <summary>当前布局作为保底，防止常规排序遗漏原有物品或降低利用率。</summary>
    public IReadOnlyList<Placement> Current { get; init; } =
        System.Array.Empty<Placement>();
}

public sealed record PackResult(
    IReadOnlyList<Placement> Placements,
    IReadOnlyList<PackItem> Unplaced)
{
    public bool Complete => Unplaced.Count == 0;

    public string Diagnostic { get; init; } = "heuristic";

    public string? Warning { get; init; }
}

/// <summary>
/// 把一组物品排进一个网格。放不下的物品原样返回，由调用方决定放弃整个网格。
/// </summary>
public interface IPacker
{
    PackResult Pack(PackRequest request, double maxSeconds = 1);
}
