using System;
using System.Linq;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Organizing;
using ChouUn.InventoryOrganizer.Core.Packing;
using Xunit;

namespace ChouUn.InventoryOrganizer.Core.Tests.Organizing;

public sealed class PackPlannerTests
{
    [Fact]
    public void 排布根与带有效tag的容器_跳过锁定与未打tag_固定占位按转置展开()
    {
        ItemSnapshot pinned = new ItemSnapshot(
            "pinned", "t", "pinned", "pinned", Array.Empty<string>(), 1, 2,
            new GridPosition(3, 0, true), false, false, LockState.Pinned, null,
            Array.Empty<GridSnapshot>());
        ItemSnapshot x = CollectPlannerTests.Leaf("x", LockState.Free);
        ItemSnapshot y = CollectPlannerTests.Leaf("y", LockState.Free);
        ItemSnapshot z = CollectPlannerTests.Leaf("z", LockState.Free);
        ItemSnapshot root = CollectPlannerTests.Root(
            CollectPlannerTests.Leaf("free", LockState.Free),
            pinned,
            CollectPlannerTests.Box("tagged", "@o ammo;", x),
            CollectPlannerTests.Box("plain", null, y),
            CollectPlannerTests.Box("empty", "@o"),
            Locked("locked", CollectPlannerTests.Box("inner", "@o ammo;", z)));

        var jobs = PackPlanner.Plan(root);

        Assert.Equal(new[] { "root", "tagged" }, jobs.Select(j => j.Container.Id));
        GridPackJob rootJob = jobs[0];
        Assert.Equal(
            new[] { "free", "tagged", "plain", "empty" },
            rootJob.FreeItems.Select(i => i.Id));
        Assert.Equal(
            new[] { new FixedBlock(3, 0, 2, 1), new FixedBlock(0, 0, 2, 2) },
            rootJob.Request.Fixed);
    }

    private static ItemSnapshot Locked(string id, params ItemSnapshot[] items)
    {
        var grids = new[] { new GridSnapshot(0, 4, 4, items) };
        return new ItemSnapshot(
            id, "t", id, id, Array.Empty<string>(), 2, 2, new GridPosition(0, 0, false),
            false, false, LockState.Locked, null, grids);
    }
}
