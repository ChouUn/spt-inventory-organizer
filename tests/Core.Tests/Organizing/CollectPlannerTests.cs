using System;
using System.Collections.Generic;
using System.Linq;
using ChouUn.StashMaster.Core.Inventory;
using ChouUn.StashMaster.Core.Organizing;
using Xunit;

namespace ChouUn.StashMaster.Core.Tests.Organizing;

public sealed class CollectPlannerTests
{
    [Fact]
    public void 目的地按优先级排序_未写的最后_无效tag另记()
    {
        ItemSnapshot inner = Box("inner", "@o#2 n:x;");
        ItemSnapshot root = Root(
            Box("late", "@o ammo;"),
            Box("first", "@o#1 n:a; @o#9 n:b;"),
            Box("broken", "@o ammo"),
            Box("outer", null, inner));
        var invalid = new List<string>();

        IReadOnlyList<Destination> destinations =
            CollectPlanner.Destinations(root, invalid);

        Assert.Equal(
            new[] { "first#1", "inner#2", "first#9", "late#" },
            destinations.Select(d => $"{d.Container.Id}#{d.Rule.Order}"));
        Assert.Equal(new[] { "broken" }, invalid);
    }

    [Fact]
    public void 候选排除锁定物品与带规则头的容器()
    {
        ItemSnapshot root = Root(
            Leaf("free", LockState.Free),
            Leaf("pinned", LockState.Pinned),
            Leaf("locked", LockState.Locked),
            Box("dest", "@o ammo;"),
            Box("broken", "@o ammo"),
            Box("plain", "只是名字"));

        Assert.Equal(
            new[] { "free", "plain" },
            CollectPlanner.Candidates(root).Select(i => i.Id));
    }

    internal static ItemSnapshot Root(params ItemSnapshot[] items)
    {
        var grids = new[] { new GridSnapshot(0, 10, 10, items) };
        return new ItemSnapshot(
            "root", "t", "仓库", "仓库", Array.Empty<string>(), 10, 10, null,
            false, false, LockState.Free, null, grids);
    }

    internal static ItemSnapshot Box(
        string id, string? tag, params ItemSnapshot[] items)
    {
        var grids = new[] { new GridSnapshot(0, 4, 4, items) };
        return new ItemSnapshot(
            id, "t", id, id, new[] { "装备", "容器" }, 2, 2, Origin,
            false, false, LockState.Free, tag, grids);
    }

    internal static ItemSnapshot Leaf(
        string id, LockState lockState, params string[] categories)
    {
        return new ItemSnapshot(
            id, "t", id, id, categories, 1, 1, Origin,
            false, false, lockState, null, Array.Empty<GridSnapshot>());
    }

    private static readonly GridPosition Origin = new GridPosition(0, 0, false);
}
