using System;
using System.Linq;
using ChouUn.StashMaster.Core.Inventory;
using Xunit;

namespace ChouUn.StashMaster.Core.Tests.Inventory;

public sealed class SnapshotDumperTests
{
    [Fact]
    public void 逐层缩进并标注位置锁与折叠()
    {
        ItemSnapshot ammo = Leaf(
            "弹药", new GridPosition(1, 0, true), LockState.Pinned);
        ItemSnapshot pouch = Container(
            "小包", new GridPosition(0, 0, false), "@o ammo;", new[] { "装备", "容器" },
            new GridSnapshot(0, 2, 2, new[] { ammo }));
        ItemSnapshot root = Container(
            "仓库", null, null, Array.Empty<string>(),
            new GridSnapshot(0, 10, 20, new[] { pouch }));

        string[] lines = SnapshotDumper.Dump(root).ToArray();

        Assert.Equal("2x2 仓库 <>", lines[0]);
        Assert.Equal("  grid0 10x20: 1 items", lines[1]);
        Assert.Equal("    (0,0) 2x2 小包 tag=\"@o ammo;\" <装备>容器>", lines[2]);
        Assert.Equal("      grid0 2x2: 1 items", lines[3]);
        Assert.Equal("        (1,0)R 1x1 弹药 [Pinned] <>", lines[4]);
    }

    private static ItemSnapshot Leaf(
        string name, GridPosition position, LockState lockState)
    {
        return new ItemSnapshot(
            name, "t", name, name, Array.Empty<string>(), 1, 1, position,
            false, false, lockState, null, Array.Empty<GridSnapshot>());
    }

    private static ItemSnapshot Container(
        string name,
        GridPosition? position,
        string? tag,
        string[] categories,
        GridSnapshot grid)
    {
        return new ItemSnapshot(
            name, "t", name, name, categories, 2, 2, position,
            false, false, LockState.Free, tag, new[] { grid });
    }
}
