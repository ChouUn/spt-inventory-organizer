using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Organizing;
using ChouUn.InventoryOrganizer.Core.Packing;
using Xunit;

namespace ChouUn.InventoryOrganizer.Core.Tests.Organizing;

public sealed class StackingTests
{
    [Fact]
    public async Task 补充Pinned并保留堆叠_不取出Pinned或改动Locked()
    {
        var port = new StackPort();
        port.Add("pinned", 40, LockState.Pinned);
        port.Add("small", 10);
        port.Add("large", 30);
        port.Add("locked", 20, LockState.Locked);
        GridPosition pinnedPosition = port.Position("pinned");
        GridPosition lockedPosition = port.Position("locked");

        await Run(port);

        Assert.Equal(60, port.Count("pinned"));
        Assert.Equal(20, port.Count("large"));
        Assert.Equal(0, port.Count("small"));
        Assert.Equal(20, port.Count("locked"));
        Assert.Equal(100, port.Total);
        Assert.Equal(pinnedPosition, port.Position("pinned"));
        Assert.Equal(lockedPosition, port.Position("locked"));
        Assert.DoesNotContain(port.Transfers, p => p.Source == "pinned"
            || p.Source == "locked" || p.Target == "locked");
    }

    [Fact]
    public async Task 合并后不超过上限且再次整理不再合并()
    {
        var port = new StackPort();
        port.Add("a", 45);
        port.Add("b", 35);
        port.Add("c", 20);
        await Run(port);

        Assert.Equal(new[] { 40, 60 }, port.Counts.OrderBy(n => n));
        Assert.Equal(100, port.Total);
        int calls = port.Transfers.Count;
        await Run(port);
        Assert.Equal(calls, port.Transfers.Count);
    }

    [Fact]
    public async Task 不合并游戏判定不同类的物品_模拟失败不扣数量()
    {
        var port = new StackPort();
        port.Add("a", 40);
        port.Add("b", 10);
        port.Add("c", 5);
        port.Incompatible.Add("c");
        port.FailSource = "b";

        OrganizeReport report = await Run(port);

        Assert.Equal(55, port.Total);
        Assert.Equal(40, port.Count("a"));
        Assert.Single(report.Failures);
        Assert.DoesNotContain(port.Transfers, p => p.Source == "c" || p.Target == "c");
    }

    private static Task<OrganizeReport> Run(StackPort port) =>
        new Organizer(port, new HeuristicPacker()).RunAsync();

    [Fact]
    public async Task 仅合并根与有效tag容器_穿过无tag父容器但跳过Locked子树()
    {
        var port = new StackPort
        {
            Tree = CollectPlannerTests.Root(
                CollectPlannerTests.Box("tagged", "@o n:never;"),
                CollectPlannerTests.Box("plain", null,
                    CollectPlannerTests.Box("nested", "@o n:never;")),
                CollectPlannerTests.Box("bad", "@o ammo"),
                CollectPlannerTests.Box("locked", "@o n:never;",
                    CollectPlannerTests.Box("insideLocked", "@o n:never;"))
                    with { Lock = LockState.Locked }),
        };
        string[] owners =
            { "root", "tagged", "plain", "nested", "bad", "locked", "insideLocked" };
        foreach (string owner in owners)
        {
            port.Add(owner + "-a", 40, owner: owner);
            port.Add(owner + "-b", 20, owner: owner);
        }

        await Run(port);

        foreach (string owner in owners)
        {
            bool merged = owner is "root" or "tagged" or "nested";
            Assert.Equal(merged ? 60 : 40, port.Count(owner + "-a"));
            Assert.Equal(merged ? 0 : 20, port.Count(owner + "-b"));
        }
        Assert.Equal(420, port.Total);
    }

    [Fact]
    public async Task Locked根容器不合并()
    {
        var port = new StackPort
        {
            Tree = CollectPlannerTests.Root() with { Lock = LockState.Locked },
        };
        port.Add("a", 40);
        port.Add("b", 20);

        await Run(port);

        Assert.Empty(port.Transfers);
        Assert.Equal(60, port.Total);
    }

    [Fact]
    public async Task 收纳不向Locked容器或其子容器补充数量()
    {
        var port = new StackPort
        {
            Tree = CollectPlannerTests.Root(
                CollectPlannerTests.Box("locked", "@o 弹药;",
                    CollectPlannerTests.Box("nested", "@o 弹药;"))
                    with { Lock = LockState.Locked }),
        };
        port.Add("source", 20);
        port.Add("lockedTarget", 30, owner: "locked");
        port.Add("nestedTarget", 30, owner: "nested");

        await Run(port);

        Assert.Empty(port.Transfers);
        Assert.Empty(port.Moves);
        Assert.Equal(20, port.Count("source"));
        Assert.Equal(30, port.Count("lockedTarget"));
        Assert.Equal(30, port.Count("nestedTarget"));
    }

    [Fact]
    public async Task 目标没有空位也先补充Pinned_来源耗尽后不再移动或排布()
    {
        var port = new StackPort
        {
            Tree = CollectPlannerTests.Root(
                CollectPlannerTests.Box("box", "@o 弹药;")),
        };
        port.FullContainers.Add("box");
        port.Add("target", 40, LockState.Pinned, "box");
        port.Add("source", 20);
        GridPosition position = port.Position("target");

        OrganizeReport report = await Run(port);

        Assert.Equal(60, port.Count("target"));
        Assert.Equal(0, port.Count("source"));
        Assert.Equal(position, port.Position("target"));
        Assert.Empty(port.Moves);
        Assert.DoesNotContain("source", port.Arranged);
        Assert.Equal(1, report.Merged);
        Assert.Equal(1, report.Moved);
        Assert.Equal(60, port.Total);
    }

    [Fact]
    public async Task 高优先级目标部分补充后无空位_剩余数量进入下一规则()
    {
        var port = new StackPort
        {
            Tree = CollectPlannerTests.Root(
                CollectPlannerTests.Box("first", "@o#1 弹药;"),
                CollectPlannerTests.Box("second", "@o#2 弹药;")),
        };
        port.FullContainers.Add("first");
        port.Add("target", 50, LockState.Pinned, "first");
        port.Add("source", 30);

        OrganizeReport report = await Run(port);

        Assert.Equal(60, port.Count("target"));
        Assert.Equal(20, port.Count("source"));
        Assert.Equal("second", port.Owner("source"));
        Assert.Equal(new[] { ("source", "first"), ("source", "second") }, port.Moves);
        Assert.Equal(1, report.Merged);
        Assert.Equal(1, report.Moved);
        Assert.Equal(80, port.Total);
        Assert.Empty(report.Failures);
    }

    private sealed class StackPort : IInventoryPort
    {
        private readonly Dictionary<string, StackSnapshot> _stacks = new();
        private readonly Dictionary<string, string> _owners = new();
        private readonly Dictionary<string, GridPosition> _positions = new();
        public ItemSnapshot Tree { get; init; } = CollectPlannerTests.Root();
        public List<(string Source, string Target)> Transfers { get; } = new();
        public List<(string Source, string Container)> Moves { get; } = new();
        public List<string> Arranged { get; } = new();
        public HashSet<string> FullContainers { get; } = new();
        public HashSet<string> Incompatible { get; } = new();
        public string? FailSource { get; set; }
        public int Total => _stacks.Values.Sum(s => s.Count);
        public IEnumerable<int> Counts => _stacks.Values.Select(s => s.Count);

        public void Add(
            string id, int count,
            LockState state = LockState.Free, string owner = "root")
        {
            _stacks.Add(id, new StackSnapshot(id, "ammo", count, 60, state));
            _owners.Add(id, owner);
            _positions.Add(id, new GridPosition(
                _owners.Count(p => p.Value == owner) - 1, 3, false));
        }

        public int Count(string id) => _stacks.TryGetValue(id, out var s) ? s.Count : 0;
        public string Owner(string id) => _owners[id];
        public GridPosition Position(string id) => _positions[id];

        public ItemSnapshot ReadSnapshot() => Populate(Tree);

        // 每次从现存堆叠重建树，让后续收纳和排布看到耗尽与部分合并后的状态。
        private ItemSnapshot Populate(ItemSnapshot container)
        {
            return container with
            {
                Grids = container.Grids.Select(grid => grid with
                {
                    Items = grid.Items.Select(Populate).Concat(
                        ReadStacks(container.Id).Select(stack =>
                            CollectPlannerTests.Leaf(stack.Id, stack.Lock, "弹药") with
                            {
                                TemplateId = stack.TemplateId,
                                Position = _positions[stack.Id],
                            })).ToArray(),
                }).ToArray(),
            };
        }

        public IReadOnlyList<StackSnapshot> ReadStacks(string containerId) =>
            _stacks.Values.Where(s => _owners[s.Id] == containerId).ToArray();

        public bool CanStack(string sourceId, string targetId) =>
            !Incompatible.Contains(sourceId) && !Incompatible.Contains(targetId);

        public Task<StackMergeResult> MergeAsync(string sourceId, string targetId)
        {
            Transfers.Add((sourceId, targetId));
            StackSnapshot source = _stacks[sourceId];
            StackSnapshot target = _stacks[targetId];
            if (sourceId == FailSource)
            {
                return Task.FromResult(new StackMergeResult(
                    0, source.Count, target.Count, "模拟失败"));
            }
            int count = Math.Min(source.Count, target.Capacity - target.Count);
            _stacks[sourceId] = source with { Count = source.Count - count };
            _stacks[targetId] = target with { Count = target.Count + count };
            if (_stacks[sourceId].Count == 0)
            {
                _stacks.Remove(sourceId);
            }
            return Task.FromResult(new StackMergeResult(
                count, source.Count - count, target.Count + count, null));
        }

        public Task<PortResult> FoldAsync(string itemId) =>
            throw new InvalidOperationException("本测试没有可折叠物品");

        public Task<PortResult> MoveAsync(string itemId, string containerId)
        {
            Moves.Add((itemId, containerId));
            Assert.True(_stacks.ContainsKey(itemId));
            if (FullContainers.Contains(containerId))
            {
                return Task.FromResult(PortResult.Fail("没有空位"));
            }
            _owners[itemId] = containerId;
            return Task.FromResult(PortResult.Ok);
        }

        public bool CanMoveToGrid(
            string itemId, string containerId, int gridIndex) => true;

        public Task<PortResult> MoveToAsync(
            string containerId, int gridIndex, Placement placement) =>
            MoveAsync(placement.Id, containerId);

        public Task<PortResult> ArrangeAsync(
            string containerId, int gridIndex, IReadOnlyList<Placement> placements)
        {
            foreach (Placement placement in placements)
            {
                if (_owners.ContainsKey(placement.Id))
                {
                    Assert.True(_stacks.ContainsKey(placement.Id));
                    Assert.Equal(LockState.Free, _stacks[placement.Id].Lock);
                    _positions[placement.Id] = new GridPosition(
                        placement.X, placement.Y, placement.Rotated);
                }
                Arranged.Add(placement.Id);
            }
            return Task.FromResult(PortResult.Ok);
        }
    }
}
