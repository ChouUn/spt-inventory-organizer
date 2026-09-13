using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChouUn.StashMaster.Core.Inventory;
using ChouUn.StashMaster.Core.Organizing;
using ChouUn.StashMaster.Core.Packing;
using Xunit;

namespace ChouUn.StashMaster.Core.Tests.Organizing;

public sealed class OrganizerTests
{
    [Fact]
    public async Task 类别顺序传到最终排布并实际应用()
    {
        ItemSnapshot[] items = new[] { "weapons", "gear", "meds" }
            .Select((category, row) =>
                CollectPlannerTests.Leaf(category, LockState.Free) with
                {
                    Position = new GridPosition(0, row, false),
                    SortType = category,
                }).ToArray();
        var port = new FakePort
        {
            Snapshot = CollectPlannerTests.Root() with
            {
                Grids = new[] { new GridSnapshot(0, 1, 3, items) },
            },
        };
        var organizer = new Organizer(port, new CachedPacker(new CpSatPacker()))
        {
            CategoryOrder = new[] { "meds", "gear", "weapons" },
        };

        OrganizeReport report = await organizer.RunAsync();

        Assert.Empty(report.Failures);
        Assert.Equal(new[] { "meds", "gear", "weapons" },
            port.Snapshot.Grids[0].Items.OrderBy(i => i.Position!.Y).Select(i => i.Id));
    }

#if DEBUG
    [Fact]
    public async Task 对比观察的是落位前输入和最终规划结果()
    {
        ItemSnapshot item = CollectPlannerTests.Leaf("far", LockState.Free)
            with { Position = new GridPosition(9, 9, false) };
        var port = new FakePort { Snapshot = CollectPlannerTests.Root(item) };
        var organizer = new Organizer(port, new HeuristicPacker());
        int observed = 0;
        organizer.FinalPackPlanned = (jobs, result, milliseconds) =>
        {
            Assert.Empty(port.Arranged);
            Assert.Equal(9, Assert.Single(jobs).Request.Current[0].Y);
            Assert.Equal(0, Assert.Single(result.Grids[0].Placements).Y);
            Assert.True(milliseconds >= 0);
            observed++;
        };

        OrganizeReport report = await organizer.RunAsync();

        Assert.Equal(1, observed);
        Assert.Equal(1, report.Packed);
        Assert.Empty(report.Failures);
    }
#endif

    [Fact]
    public async Task 折叠阶段逐项执行并汇总失败()
    {
        var port = new FakePort
        {
            Snapshot = CollectPlannerTests.Root(
                FoldPlannerTests.Item("a", true, false, LockState.Free),
                FoldPlannerTests.Item("b", true, false, LockState.Free),
                FoldPlannerTests.Item("c", false, false, LockState.Free)),
            FailingFold = "b",
        };

        OrganizeReport report = await Run(port);

        Assert.Equal(new[] { "a", "b" }, port.Folded);
        Assert.Equal(1, report.Folded);
        Assert.Equal(new[] { "折叠 b：no room" }, report.Failures);
    }

    [Fact]
    public async Task 收纳按优先级尝试_放不下的留给后面的规则()
    {
        var port = new FakePort
        {
            Snapshot = CollectPlannerTests.Root(
                CollectPlannerTests.Leaf("mag1", LockState.Free, "弹匣"),
                CollectPlannerTests.Leaf("mag2", LockState.Free, "弹匣"),
                CollectPlannerTests.Leaf("med", LockState.Free, "医疗"),
                CollectPlannerTests.Leaf("pinnedMag", LockState.Pinned, "弹匣"),
                CollectPlannerTests.Box("small", "@o#1 弹匣;"),
                CollectPlannerTests.Box("big", "@o#5 弹匣;"),
                CollectPlannerTests.Box("bad", "@o 弹匣")),
        };
        port.Full.Add(("mag2", "small"));

        OrganizeReport report = await Run(port);

        Assert.Equal(
            new[] { "mag1->small", "mag2->small", "mag2->big" },
            port.Moves);
        Assert.Equal(2, report.Moved);
        Assert.Equal(new[] { "容器 bad 的 tag 无效，未参与收纳" }, report.Warnings);
    }

    [Fact]
    public async Task 排布只动布局有变化的网格()
    {
        ItemSnapshot movedAway = new ItemSnapshot(
            "far", "t", "far", "far", System.Array.Empty<string>(), 1, 1,
            new GridPosition(9, 9, false), false, false, LockState.Free, null,
            System.Array.Empty<GridSnapshot>());
        var port = new FakePort
        {
            Snapshot = CollectPlannerTests.Root(movedAway),
        };

        OrganizeReport report = await Run(port);

        Assert.Equal(new[] { "root:0:1" }, port.Arranged);
        Assert.Equal(1, report.Packed);
    }

    private static Task<OrganizeReport> Run(FakePort port)
    {
        return new Organizer(port, new HeuristicPacker()).RunAsync();
    }

    [Fact]
    public async Task 排布落位后再次整理不产生事务且固定占位不变()
    {
        ItemSnapshot pinned = CollectPlannerTests.Leaf("pinned", LockState.Pinned);
        ItemSnapshot locked = CollectPlannerTests.Leaf("locked", LockState.Locked)
            with { Position = new GridPosition(1, 0, false) };
        var port = new FakePort
        {
            Snapshot = CollectPlannerTests.Root(
                CollectPlannerTests.Leaf("b", LockState.Free)
                    with { Position = new GridPosition(9, 9, true) },
                pinned,
                locked,
                CollectPlannerTests.Leaf("a", LockState.Free)
                    with { Position = new GridPosition(8, 9, false) }),
        };

        Assert.Equal(1, (await Run(port)).Packed);
        Assert.Equal(0, (await Run(port)).Packed);
        Assert.Single(port.Arranged);
        Assert.Equal(pinned,
            port.Snapshot.Grids[0].Items.Single(i => i.Id == "pinned"));
        Assert.Equal(locked,
            port.Snapshot.Grids[0].Items.Single(i => i.Id == "locked"));
    }

    private sealed class FakePort : IInventoryPort
    {
        public ItemSnapshot Snapshot { get; set; } = null!;

        public string? FailingFold { get; set; }

        public HashSet<(string Item, string Container)> Full { get; } = new();

        public List<string> Folded { get; } = new List<string>();

        public List<string> Moves { get; } = new List<string>();

        public ItemSnapshot ReadSnapshot() => Snapshot;

        public IReadOnlyList<StackSnapshot> ReadStacks(string containerId) =>
            System.Array.Empty<StackSnapshot>();

        public bool CanStack(string sourceId, string targetId) => false;

        public Task<StackMergeResult> MergeAsync(string sourceId, string targetId) =>
            throw new System.InvalidOperationException("本测试没有可堆叠物品");

        public Task<PortResult> FoldAsync(string itemId)
        {
            Folded.Add(itemId);
            PortResult result = itemId == FailingFold
                ? PortResult.Fail("no room")
                : PortResult.Ok;
            return Task.FromResult(result);
        }

        public Task<PortResult> MoveAsync(string itemId, string containerId)
        {
            Moves.Add($"{itemId}->{containerId}");
            PortResult result = Full.Contains((itemId, containerId))
                ? PortResult.Fail("没有空位")
                : PortResult.Ok;
            return Task.FromResult(result);
        }

        public bool CanMoveToGrid(
            string itemId, string containerId, int gridIndex) => true;

        public Task<PortResult> MoveToAsync(
            string containerId, int gridIndex, Placement placement) =>
            MoveAsync(placement.Id, containerId);

        public List<string> Arranged { get; } = new List<string>();

        public Task<PortResult> ArrangeAsync(
            string containerId, int gridIndex, IReadOnlyList<Placement> placements)
        {
            Arranged.Add($"{containerId}:{gridIndex}:{placements.Count}");
            // 本测试替身把根网格真实更新，后续快照才能检验重复整理的稳定性。
            Assert.Equal(Snapshot.Id, containerId);
            var positions = placements.ToDictionary(p => p.Id);
            Snapshot = Snapshot with
            {
                Grids = Snapshot.Grids.Select(grid => grid.Index != gridIndex
                    ? grid
                    : grid with
                    {
                        Items = grid.Items.Select(item =>
                            positions.TryGetValue(item.Id, out Placement? p)
                                ? item with
                                {
                                    Position = new GridPosition(p.X, p.Y, p.Rotated),
                                }
                                : item).ToArray(),
                    }).ToArray(),
            };
            return Task.FromResult(PortResult.Ok);
        }
    }
}
