using System;
using System.Collections.Generic;
using System.Linq;
using ChouUn.StashMaster.Core.Inventory;
using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.InventoryLogic;

namespace ChouUn.StashMaster.Adapter;

/// <summary>把游戏里的物品树读成纯数据快照。</summary>
internal static class SnapshotReader
{
    public static ItemSnapshot Read(Item item)
    {
        IntVec2 size = item.CalculateCellSize();
        bool canFold = ItemManipulator.CanFold(item, out FoldableComponent foldable);
        string sortType = ItemTypeClassifier.Classify(item);
        return new ItemSnapshot(
            item.Id,
            item.StringTemplateId,
            item.LocalizedName(),
            item.LocalizedShortName(),
            Categories(item),
            size.X,
            size.Y,
            Position(item),
            canFold,
            foldable != null && foldable.Folded,
            ToLockState(item.PinLockState),
            Tag(item),
            Grids(item))
        {
            SortType = sortType,
            SubcategoryPath = ItemCategoryHierarchy.Subcategories(sortType, CategoryPath(item)),
        };
    }

    /// <summary>读取包含 mod 注册结果的运行时手册链，交给类型边界转换。</summary>
    private static IReadOnlyList<string> CategoryPath(Item item)
    {
        Handbook? handbook = Singleton<Handbook>.Instance;
        if (handbook?.AllNodes == null || !handbook.AllNodes.TryGetValue(
            item.StringTemplateId, out HandbookNode node))
        {
            return Array.Empty<string>();
        }
        var path = new List<string>();
        for (HandbookNode? parent = node.Parent; parent != null; parent = parent.Parent)
        {
            path.Add(parent.Data.Id);
        }
        path.Reverse();
        return path;
    }

    private static GridPosition? Position(Item item)
    {
        if (item.CurrentAddress is not GridItemAddress address)
        {
            return null;
        }
        LocationInGrid location = address.LocationInGrid;
        bool rotated = location.r == ItemRotation.Vertical;
        return new GridPosition(location.x, location.y, rotated);
    }

    private static string? Tag(Item item)
    {
        bool hasTag = item.TryGetItemComponent(out TagComponent tagComponent);
        return hasTag && !tagComponent.IsEmpty ? tagComponent.Name : null;
    }

    private static IReadOnlyList<GridSnapshot> Grids(Item item)
    {
        if (item is not CompoundItem compound || compound.Grids == null)
        {
            return Array.Empty<GridSnapshot>();
        }
        return compound.Grids.Select(ReadGrid).ToList();
    }

    private static GridSnapshot ReadGrid(Grid grid, int index)
    {
        List<ItemSnapshot> items = grid.Items.Select(Read).ToList();
        return new GridSnapshot(index, grid.GridWidth, grid.GridHeight, items);
    }

    /// <summary>手册里该物品的类别链，逐级本地化。手册未加载或找不到时为空。</summary>
    private static IReadOnlyList<string> Categories(Item item)
    {
        Handbook? handbook = Singleton<Handbook>.Instance;
        if (handbook?.AllNodes == null)
        {
            return Array.Empty<string>();
        }
        string templateId = item.StringTemplateId;
        if (!handbook.AllNodes.TryGetValue(templateId, out HandbookNode node))
        {
            return Array.Empty<string>();
        }
        return node.Category.Select(id => id.Localized()).ToList();
    }

    internal static LockState ToLockState(EItemPinLockState state)
    {
        switch (state)
        {
            case EItemPinLockState.Pinned:
                return LockState.Pinned;
            case EItemPinLockState.Locked:
                return LockState.Locked;
            default:
                return LockState.Free;
        }
    }
}
