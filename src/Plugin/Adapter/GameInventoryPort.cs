using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ChouUn.StashMaster.Core.Inventory;
using ChouUn.StashMaster.Core.Organizing;
using ChouUn.StashMaster.Core.Packing;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;

namespace ChouUn.StashMaster.Adapter;

/// <summary>用游戏 API 实现编排层的出口：先模拟，再交给网络事务执行并同步。</summary>
internal sealed class GameInventoryPort : IInventoryPort
{
    private readonly CompoundItem _root;
    private readonly InventoryController _controller;
    private Dictionary<string, Item> _items = new Dictionary<string, Item>();

    public GameInventoryPort(CompoundItem root, InventoryController controller)
    {
        _root = root;
        _controller = controller;
    }

    public ItemSnapshot ReadSnapshot()
    {
        var stopwatch = Stopwatch.StartNew();
        _items = _root.GetAllItems().ToDictionary(item => item.Id);
        ItemSnapshot snapshot = SnapshotReader.Read(_root);
        Plugin.Log.LogInfo(
            $"snapshot: {_items.Count} items, {stopwatch.ElapsedMilliseconds} ms");
        return snapshot;
    }

    public async Task<PortResult> FoldAsync(string itemId)
    {
        if (!_items.TryGetValue(itemId, out Item item))
        {
            return PortResult.Fail("物品已不在范围内");
        }
        if (!ItemManipulator.CanFold(item, out FoldableComponent foldable))
        {
            return PortResult.Fail("当前不可折叠");
        }
        OperationResult<FoldResult> simulated =
            ItemManipulator.Fold(foldable, folded: true, simulate: true);
        if (!simulated.Succeeded)
        {
            return PortResult.Fail(Describe(simulated.Error));
        }
        return await CommitAsync(simulated);
    }

    public IReadOnlyList<StackSnapshot> ReadStacks(string containerId)
    {
        if (!_items.TryGetValue(containerId, out Item target)
            || target is not CompoundItem container
            || container.PinLockState == EItemPinLockState.Locked)
        {
            return Array.Empty<StackSnapshot>();
        }
        return container.Grids.SelectMany(grid => grid.Items)
            .Where(item => item.StackMaxSize > 1 && item.StackObjectsCount > 0)
            .Select(item => new StackSnapshot(
                item.Id, item.StringTemplateId,
                item.StackObjectsCount, item.StackMaxSize,
                SnapshotReader.ToLockState(item.PinLockState)))
            .ToArray();
    }

    public bool CanMoveToGrid(string itemId, string containerId, int gridIndex)
    {
        return _items.TryGetValue(itemId, out Item item)
            && item.PinLockState == EItemPinLockState.Free
            && item.CurrentAddress != null
            && ItemManipulator.CanModifyItem(item, item.Parent, _controller, out _)
            && _items.TryGetValue(containerId, out Item target)
            && target is CompoundItem container
            && gridIndex < container.Grids.Length
            && container.Grids[gridIndex].CheckCompatibility(item)
            && ItemManipulator.CanTransferTo(container.Grids[gridIndex]
                .CreateItemAddress(new LocationInGrid(0, 0, ItemRotation.Horizontal)),
                _controller, out _);
    }

    public async Task<PortResult> MoveToAsync(
        string containerId, int gridIndex, Placement placement)
    {
        if (!CanMoveToGrid(placement.Id, containerId, gridIndex))
        {
            return PortResult.Fail("游戏不允许移入目标网格");
        }
        var container = (CompoundItem)_items[containerId];
        var location = new LocationInGrid(placement.X, placement.Y,
            placement.Rotated ? ItemRotation.Vertical : ItemRotation.Horizontal);
        OperationResult<MoveResult> simulated = ItemManipulator.Move(
            _items[placement.Id],
            container.Grids[gridIndex].CreateItemAddress(location),
            _controller, simulate: true);
        return simulated.Succeeded
            ? await CommitAsync(simulated)
            : PortResult.Fail(Describe(simulated.Error));
    }

    public bool CanStack(string sourceId, string targetId)
    {
        return _items.TryGetValue(sourceId, out Item source)
            && _items.TryGetValue(targetId, out Item target)
            && source.IsSameItem(target);
    }

    /// <summary>
    /// IsSameItem 保留游戏当前生效的同类判定（含 FiR 兼容补丁），
    /// TransferOrMerge 校验双方限制及容量；不调用任何排序入口。
    /// </summary>
    public async Task<StackMergeResult> MergeAsync(string sourceId, string targetId)
    {
        if (!_items.TryGetValue(sourceId, out Item source)
            || !_items.TryGetValue(targetId, out Item target)
            || source.CurrentAddress is null || target.CurrentAddress is null)
        {
            return new StackMergeResult(0, 0, 0, "堆叠已不在整理范围内");
        }
        int before = source.StackObjectsCount;
        if (source.PinLockState != EItemPinLockState.Free
            || target.PinLockState == EItemPinLockState.Locked
            || !source.IsSameItem(target))
        {
            return new StackMergeResult(
                0, before, target.StackObjectsCount, "锁定或不同类的堆叠");
        }
        OperationResult<ITransferOrMergeResult> simulated =
            ItemManipulator.TransferOrMerge(
                source, target, _controller, simulate: true);
        if (simulated.Failed)
        {
            return new StackMergeResult(
                0, before, target.StackObjectsCount, Describe(simulated.Error));
        }
        PortResult committed = await CommitAsync(simulated);
        int remaining = source.CurrentAddress is null ? 0 : source.StackObjectsCount;
        if (committed.Succeeded && remaining == 0)
        {
            _items.Remove(sourceId);
        }
        int transferred = committed.Succeeded ? before - remaining : 0;
        Plugin.Log.LogInfo(
            $"merge {sourceId} -> {targetId}: {transferred} transferred, " +
            $"source {remaining}, target {target.StackObjectsCount}");
        return new StackMergeResult(
            transferred, remaining, target.StackObjectsCount, committed.Error);
    }

    /// <summary>
    /// 与原生排序同一套路：在内存里摘下要动的物品，按布局放回，回滚到原状，
    /// 再把记录了目标地址的结果交给事务；事务会重放这些地址并把位置同步给服务端。
    /// </summary>
    public async Task<PortResult> ArrangeAsync(
        string containerId, int gridIndex, IReadOnlyList<Placement> placements)
    {
        _items.TryGetValue(containerId, out Item target);
        if (target is not CompoundItem container || gridIndex >= container.Grids.Length)
        {
            return PortResult.Fail("目标网格已不在范围内");
        }
        Grid grid = container.Grids[gridIndex];
        var stopwatch = Stopwatch.StartNew();
        var moving = new List<Item>();
        foreach (Placement placement in placements)
        {
            if (!_items.TryGetValue(placement.Id, out Item item) || !grid.Contains(item)
                || item.PinLockState != EItemPinLockState.Free)
            {
                return PortResult.Fail("布局与网格内容不一致");
            }
            moving.Add(item);
        }

        var removes = new List<ContainerRemoveResult>();
        var adds = new List<GridAddResult>();
        string? error = null;
        foreach (Item item in moving)
        {
            OperationResult<ContainerRemoveResult> removed =
                grid.Remove(item, simulate: false);
            if (removed.Failed)
            {
                error = Describe(removed.Error);
                break;
            }
            removes.Add(removed.Value);
        }
        if (error is null)
        {
            foreach (Placement placement in placements)
            {
                ItemRotation rotation = placement.Rotated
                    ? ItemRotation.Vertical
                    : ItemRotation.Horizontal;
                var location = new LocationInGrid(placement.X, placement.Y, rotation);
                OperationResult<GridAddResult> added =
                    grid.Add(_items[placement.Id], location, simulate: false);
                if (added.Failed)
                {
                    error = Describe(added.Error);
                    break;
                }
                adds.Add(added.Value);
            }
        }

        for (int i = adds.Count - 1; i >= 0; i--)
        {
            adds[i].RollBack();
        }
        for (int i = removes.Count - 1; i >= 0; i--)
        {
            removes[i].RollBack();
        }
        if (error != null)
        {
            return PortResult.Fail(error);
        }
        long validateMs = stopwatch.ElapsedMilliseconds;
        OperationResult<ApplySortItemsPositionResult> arranged =
            new ApplySortItemsPositionResult(container, removes, adds, _controller);
        PortResult committed = await CommitAsync(arranged);
        long commitMs = stopwatch.ElapsedMilliseconds - validateMs;
        Plugin.Log.LogInfo(
            $"arrange {container.LocalizedName()} grid {gridIndex}: " +
            $"{placements.Count} moved, validate {validateMs} ms, " +
            $"commit {commitMs} ms");
        if (committed.Succeeded)
        {
            LogMismatches(grid, placements);
        }
        return committed;
    }

    /// <summary>诊断：提交后逐项比对实际位置与请求位置，不一致的记日志。</summary>
    private void LogMismatches(Grid grid, IReadOnlyList<Placement> placements)
    {
        int mismatches = 0;
        foreach (Placement placement in placements)
        {
            Item item = _items[placement.Id];
            LocationInGrid actual = grid.GetItemLocation(item);
            bool rotated = actual != null && actual.r == ItemRotation.Vertical;
            if (actual == null || actual.x != placement.X || actual.y != placement.Y
                || rotated != placement.Rotated)
            {
                mismatches++;
                if (mismatches <= 5)
                {
                    string got = actual == null
                        ? "missing"
                        : $"({actual.x},{actual.y},{actual.r})";
                    Plugin.Log.LogWarning(
                        $"mismatch {item.LocalizedName()}: wanted " +
                        $"({placement.X},{placement.Y},{placement.Rotated}) got {got}");
                }
            }
        }
        if (mismatches > 0)
        {
            Plugin.Log.LogWarning(
                $"arrange mismatches: {mismatches}/{placements.Count}");
        }
    }

    private async Task<PortResult> CommitAsync(OperationResult simulated)
    {
        try
        {
            IResult result = await _controller.TryRunNetworkTransaction(simulated);
            return string.IsNullOrEmpty(result.Error)
                ? PortResult.Ok
                : PortResult.Fail(result.Error);
        }
        catch (Exception ex)
        {
            return PortResult.Fail(ex.Message);
        }
    }

    private static string Describe(Error error)
    {
        return error is InventoryError inventoryError
            ? inventoryError.GetLocalizedDescription()
            : error.ToString();
    }
}
