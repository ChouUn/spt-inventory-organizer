using System;
using System.Linq;
using ChouUn.StashMaster.Core.Inventory;
using ChouUn.StashMaster.Core.Organizing;
using Xunit;

namespace ChouUn.StashMaster.Core.Tests.Organizing;

public sealed class FoldPlannerTests
{
    [Fact]
    public void 只选未折叠且未锁死的可折叠物品_含嵌套_不含根()
    {
        ItemSnapshot nestedRifle = Item("nested", true, false, LockState.Free);
        ItemSnapshot bag = Item(
            "bag", true, false, LockState.Free,
            new GridSnapshot(0, 4, 4, new[] { nestedRifle }));
        ItemSnapshot root = Item(
            "root", true, false, LockState.Free,
            new GridSnapshot(0, 10, 10, new[]
            {
                Item("free", true, false, LockState.Free),
                Item("pinned", true, false, LockState.Pinned),
                Item("locked", true, false, LockState.Locked),
                Item("already", true, true, LockState.Free),
                Item("rigid", false, false, LockState.Free),
                bag,
            }));

        string[] ids = FoldPlanner.Plan(root).Select(i => i.Id).ToArray();

        Assert.Equal(new[] { "free", "pinned", "bag", "nested" }, ids);
    }

    internal static ItemSnapshot Item(
        string id,
        bool canFold,
        bool folded,
        LockState lockState,
        params GridSnapshot[] grids)
    {
        return new ItemSnapshot(
            id, "t", id, id, Array.Empty<string>(), 1, 1, new GridPosition(0, 0, false),
            canFold, folded, lockState, null, grids);
    }
}
