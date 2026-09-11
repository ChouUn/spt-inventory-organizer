#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using ChouUn.StashMaster.Core.Inventory;
using ChouUn.StashMaster.Core.Organizing;
using ChouUn.StashMaster.Core.Packing;
using ChouUn.StashMaster.Diagnostics;
using EFT.InventoryLogic;
using HarmonyLib;
using Xunit;

namespace ChouUn.StashMaster.Plugin.Tests;

public sealed class SortSimulationTests
{
    private static int _patchedCalls;

    [Fact]
    public void 原生基准绕过全部空位补丁并保留普通调用的补丁()
    {
        var item = new Item("000000000000000000000001", new ItemTemplate
        {
            _id = "000000000000000000000002", Width = 2, Height = 1,
        });
        var harmony = new Harmony("test.native-search-chain");
        var methods = new[]
        {
            AccessTools.Method(typeof(Grid), nameof(Grid.FindFreeSpace)),
            AccessTools.Method(typeof(Grid), nameof(Grid.FindFreeSpaceInGrid)),
            AccessTools.Method(typeof(Grid), nameof(Grid.GetFreeLocation)),
        };
        try
        {
            foreach (var method in methods)
                harmony.Patch(method, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(SortSimulationTests), nameof(Reject))));
            var grid = new Grid("patched", 3, 3, false, false,
                Array.Empty<ItemFilter>(), null!);
            Assert.Null(grid.FindFreeSpace(item));
            _patchedCalls = 0;

            NativeGridSearch.Prepare();
            PackResult result = SortSimulation.Run(Job(item),
                new Dictionary<string, Item> { [item.Id] = item },
                i => i.ToList(), NativeGridSearch.Find);

            Assert.True(result.Complete);
            Assert.Equal(new Placement(item.Id, 0, 1, true),
                Assert.Single(result.Placements));
            Assert.Equal(0, _patchedCalls);
            foreach (var method in methods)
                Assert.Contains(harmony.Id, Harmony.GetPatchInfo(method).Owners);
            Assert.Null(grid.FindFreeSpace(item));
            Assert.Equal(1, _patchedCalls);
        }
        finally
        {
            foreach (var method in methods)
                harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
        }
    }

    private static bool Reject(ref LocationInGrid? __result)
    {
        _patchedCalls++;
        __result = null;
        return false;
    }

    [Fact]
    public void 基准成功或失败均不写物品地址数量和原网格()
    {
        var item = new Item("000000000000000000000001", new ItemTemplate
        {
            _id = "000000000000000000000002", Width = 2, Height = 1,
            StackObjectsCount = 17, StackMaxSize = 60,
        });
        var parent = new CompoundItem("000000000000000000000003",
            new CompoundItemTemplate { _id = "000000000000000000000004" });
        var grid = new Grid("original", 3, 3, false, false,
            Array.Empty<ItemFilter>(), parent);
        grid.PlaceItem(item, new LocationInGrid(1, 2, ItemRotation.Horizontal));
        ItemAddress address = item.CurrentAddress;
        int[] horizontal = grid.Horizontal.ToArray();
        int[] vertical = grid.Vertical.ToArray();
        GridPackJob job = Job(item);
        var items = new Dictionary<string, Item> { [item.Id] = item };

        PackResult success = SortSimulation.Run(job, items, i => i.ToList(),
            (g, i) => g.FindFreeSpace(i));
        Assert.True(success.Complete);
        Assert.Equal(new Placement(item.Id, 0, 1, true),
            Assert.Single(success.Placements));
        PackResult failure = SortSimulation.Run(job, items, i => i.ToList(),
            (_, _) => null);
        Assert.False(failure.Complete);
        Assert.Equal(item.Id, Assert.Single(failure.Unplaced).Id);
        Assert.Throws<InvalidOperationException>(() => SortSimulation.Run(
            job, items, _ => throw new InvalidOperationException("test"),
            (g, i) => g.FindFreeSpace(i)));

        Assert.Same(address, item.CurrentAddress);
        Assert.Equal(17, item.StackObjectsCount);
        Assert.Same(item, Assert.Single(grid.Items));
        Assert.Equal(horizontal, grid.Horizontal);
        Assert.Equal(vertical, grid.Vertical);
    }

    private static GridPackJob Job(Item item)
    {
        var snapshot = new ItemSnapshot(item.Id, item.StringTemplateId, "item",
            "item", Array.Empty<string>(), 2, 1, new GridPosition(1, 2, false),
            false, false, LockState.Free, null, Array.Empty<GridSnapshot>());
        var request = new PackRequest(1, 3,
            new[] { new FixedBlock(0, 0, 1, 1) },
            new[] { new PackItem(item.Id, item.StringTemplateId, 2, 1) });
        return new GridPackJob(snapshot, 0, request, new[] { snapshot });
    }
}
#endif
