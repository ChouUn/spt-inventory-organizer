using System.Collections.Generic;

namespace ChouUn.InventoryOrganizer.Core.Packing;

/// <summary>待排布的物品，尺寸为未转置的格子数。</summary>
public sealed record PackItem(string Id, string TemplateId, int Width, int Height);

/// <summary>网格里已被固定物品占住的矩形，尺寸已按其当前转置展开。</summary>
public sealed record FixedBlock(int X, int Y, int Width, int Height);

public sealed record Placement(string Id, int X, int Y, bool Rotated);

public sealed record PackRequest(
    int Width,
    int Height,
    IReadOnlyList<FixedBlock> Fixed,
    IReadOnlyList<PackItem> Items);

public sealed record PackResult(
    IReadOnlyList<Placement> Placements,
    IReadOnlyList<PackItem> Unplaced)
{
    public bool Complete => Unplaced.Count == 0;
}

/// <summary>
/// 把一组物品排进一个网格。放不下的物品原样返回，由调用方决定放弃整个网格。
/// </summary>
public interface IPacker
{
    PackResult Pack(PackRequest request);
}
