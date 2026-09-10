using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Organizing;
using ChouUn.InventoryOrganizer.Core.Packing;
using ChouUn.InventoryOrganizer.Core.Tests.Packing;
using Xunit;

namespace ChouUn.InventoryOrganizer.Core.Tests.Organizing;

public sealed class CollectPackingTests
{
    [Fact]
    public async Task 联合收纳落入正确网格_原有物品与数量保留()
    {
        var port = new PackingPort();
        port.Box("box", 3, 3, "@o 物品;");
        port.AddGrid("box", 2, 2);
        port.Item("square", 2, 2);
        port.Item("bar1", 1, 3);
        port.Item("bar2", 1, 3);
        port.Item("bar3", 1, 3);

        OrganizeReport report = await Run(port);

        Assert.Equal(4, report.Moved);
        Assert.Equal(1, port.GridOf("square"));
        Assert.All(new[] { "bar1", "bar2", "bar3" }, id =>
            Assert.Equal(0, port.GridOf(id)));
        Assert.Equal(4, port.Contents("box").Count);
        Assert.Empty(report.Failures);
        port.AssertValid();
    }

    [Fact]
    public async Task 联合布局一个网格腾位失败_另一个成功_余量继续后续规则()
    {
        var port = new PackingPort { FailingArrange = "first", FailingGrid = 0 };
        port.Box("first", 4, 4, "@o#1 物品;");
        port.AddGrid("first", 1, 1);
        port.Box("second", 1, 4, "@o#2 物品;");
        port.Item("large", 2, 3, "first", 0, 0);
        port.Item("square", 2, 2, "first", 2, 2);
        port.Item("bar", 1, 4);
        port.Item("single", 1, 1);
        port.GridDenied.Add(("single", "first", 0));

        OrganizeReport report = await Run(port);

        Assert.Equal("second", port.Owner("bar"));
        Assert.Equal("first", port.Owner("single"));
        Assert.Equal(1, port.GridOf("single"));
        Assert.DoesNotContain("move bar -> first", port.Events);
        Assert.NotEmpty(report.Failures);
        port.AssertValid();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public async Task 规则增加不重复读取全树或堆叠(int ruleCount)
    {
        var port = new PackingPort();
        port.Box("box", 1, 1, string.Join(" ",
            Enumerable.Repeat("@o 物品;", ruleCount)));
        port.Item("item", 1, 1);
        port.Denied.Add("item");

        OrganizeReport report = await Run(port);

        Assert.Equal(4, port.SnapshotReads);
        Assert.Equal(2, port.StackReads);
        Assert.Equal(port.SnapshotReads, report.SnapshotReads);
        Assert.Equal(port.StackReads, report.StackReads);
        Assert.Equal(1, report.RuleMatches);
        Assert.Equal("root", port.Owner("item"));
        Assert.Empty(report.Failures);
    }

    [Fact]
    public async Task 收纳前重排碎片空位_原有物品保留_候选全部移入()
    {
        var port = new PackingPort();
        port.Box("box", 4, 4, "@o 物品;");
        port.Item("large", 2, 3, "box", 0, 0);
        port.Item("square", 2, 2, "box", 2, 2);
        port.Item("bar", 1, 4);
        port.Item("single1", 1, 1);
        port.Item("single2", 1, 1);

        OrganizeReport report = await Run(port);

        Assert.Equal(3, report.Moved);
        Assert.Equal(5, port.Contents("box").Count);
        Assert.Equal("arrange box", port.Events.First());
        Assert.Equal("box", port.Owner("large"));
        Assert.Equal("box", port.Owner("square"));
        Assert.Empty(report.Failures);
        port.AssertValid();
        int events = port.Events.Count;
        await Run(port);
        Assert.Equal(events, port.Events.Count);
    }

    [Fact]
    public async Task 同一规则选面积更多的组合_未选中候选再进低优先级容器()
    {
        var port = new PackingPort();
        port.Box("first", 3, 3, "@o#1 物品;");
        port.Box("second", 2, 2, "@o#2 物品;");
        port.Item("square", 2, 2);
        port.Item("bar1", 1, 3);
        port.Item("bar2", 1, 3);
        port.Item("bar3", 1, 3);

        OrganizeReport report = await Run(port);

        Assert.Equal(new[] { "bar1", "bar2", "bar3" },
            port.Contents("first").OrderBy(id => id));
        Assert.Equal("second", port.Owner("square"));
        Assert.Equal(4, report.Moved);
        Assert.Empty(report.Failures);
        port.AssertValid();
    }

    [Fact]
    public async Task 不为提高面积丢掉原有物品_固定位置不变_过滤由游戏决定()
    {
        var port = new PackingPort();
        port.Box("box", 3, 3, "@o 物品;");
        port.Item("existing", 2, 2, "box", 0, 0);
        port.Item("pinned", 1, 1, "box", 2, 2, LockState.Pinned);
        port.Item("bar1", 1, 3);
        port.Item("bar2", 1, 3);
        port.Item("denied", 1, 1);
        port.Denied.Add("denied");

        await Run(port);

        Assert.Equal("box", port.Owner("existing"));
        Assert.Equal(new GridPosition(2, 2, false), port.Position("pinned"));
        Assert.Equal("root", port.Owner("denied"));
        Assert.Equal(1, new[] { "bar1", "bar2" }.Count(id => port.Owner(id) == "box"));
        port.AssertValid();
    }

    [Fact]
    public async Task 腾位置失败不移动候选_仍可尝试下一条规则()
    {
        var port = new PackingPort { FailingArrange = "first" };
        port.Box("first", 4, 4, "@o#1 物品;");
        port.Box("second", 4, 4, "@o#2 物品;");
        port.Item("large", 2, 3, "first", 0, 0);
        port.Item("square", 2, 2, "first", 2, 2);
        port.Item("bar", 1, 4);

        OrganizeReport report = await Run(port);

        Assert.Equal("second", port.Owner("bar"));
        Assert.DoesNotContain("move bar -> first", port.Events);
        Assert.NotEmpty(report.Failures);
        port.AssertValid();
    }

    private static Task<OrganizeReport> Run(PackingPort port) =>
        new Organizer(port, new CpSatPacker()).RunAsync();

    [Theory]
    [InlineData(false, "模拟拒绝")]
    [InlineData(true, "网络事务失败")]
    public async Task 移动失败记录物品目标和原因_候选仍可进入后续规则(
        bool hasNextRule, string error)
    {
        var port = new PackingPort { FailingMove = "first", MoveError = error };
        port.Box("first", 2, 2, "@o#1 物品;");
        if (hasNextRule)
        {
            port.Box("second", 2, 2, "@o#2 物品;");
        }
        port.Item("item", 1, 1);

        OrganizeReport report = await Run(port);

        Assert.Equal($"收纳 item (item) -> first (first) 网格 0：{error}",
            Assert.Single(report.Failures));
        Assert.Equal(hasNextRule ? 1 : 0, report.Moved);
        Assert.Equal(hasNextRule ? "second" : "root", port.Owner("item"));
        Assert.Equal(hasNextRule ? 2 : 1, port.MoveAttempts);
        port.AssertValid();
    }

    [Fact]
    public async Task 多网格先统一准备保底_不逐个分配搜索时间_求解不在调用上下文()
    {
        var port = new PackingPort();
        for (int i = 0; i < 4; i++)
        {
            port.Box("box" + i, 4, 4, "@o n:never;");
            port.Item("item" + i, 1, 2, "box" + i);
        }
        var packer = new BudgetPacker();

        OrganizeReport report = await new Organizer(port, packer).RunAsync();

        Assert.Equal(4, packer.Budgets.Count);
        Assert.All(packer.Budgets, b => Assert.Equal(0, b));
        Assert.All(packer.Contexts, Assert.Null);
        Assert.Single(report.Diagnostics, d => d.Contains("stage=final"));
        Assert.Contains(report.Diagnostics, d => d.Contains("allotted=3.000s"));
    }

    private sealed class BudgetPacker : IPacker
    {
        public List<double> Budgets { get; } = new();
        public List<SynchronizationContext?> Contexts { get; } = new();

        public PackResult Pack(PackRequest request, double maxSeconds = 1)
        {
            Budgets.Add(maxSeconds);
            Contexts.Add(SynchronizationContext.Current);
            return new HeuristicPacker().Pack(request);
        }
    }

    /// <summary>带真实占位检查的内存库存，覆盖腾位置与精确移动之间的衔接。</summary>
    private sealed class PackingPort : IInventoryPort
    {
        private readonly Dictionary<string, ItemSnapshot> _items = new();
        private readonly Dictionary<string, string> _owners = new();
        private readonly Dictionary<string, int> _grids = new();
        public List<string> Events { get; } = new();
        public HashSet<string> Denied { get; } = new();
        public HashSet<(string Item, string Container, int Grid)> GridDenied { get; }
            = new();
        public int SnapshotReads { get; private set; }
        public int StackReads { get; private set; }
        public string? FailingArrange { get; init; }
        public int? FailingGrid { get; init; }
        public string? FailingMove { get; init; }
        public string MoveError { get; init; } = "模拟拒绝";
        public int MoveAttempts { get; private set; }

        public PackingPort() => _items.Add("root", CollectPlannerTests.Root());

        public void Box(string id, int width, int height, string tag)
        {
            _items.Add(id, CollectPlannerTests.Box(id, tag) with
            {
                Position = new GridPosition(_owners.Count * 2, 0, false),
                Grids = new[] { new GridSnapshot(0, width, height,
                    Array.Empty<ItemSnapshot>()) },
                Lock = LockState.Pinned,
            });
            _owners.Add(id, "root");
            _grids.Add(id, 0);
        }

        public void AddGrid(string container, int width, int height)
        {
            ItemSnapshot item = _items[container];
            _items[container] = item with
            {
                Grids = item.Grids.Concat(new[] { new GridSnapshot(
                    item.Grids.Count, width, height, Array.Empty<ItemSnapshot>()) })
                    .ToArray(),
            };
        }

        public int GridOf(string item) => _grids[item];

        public void Item(string id, int width, int height, string owner = "root",
            int x = 0, int y = 0, LockState state = LockState.Free)
        {
            if (owner == "root")
            {
                int containers = _items.Values.Count(i => i.Grids.Count > 0);
                x = (_owners.Count - containers + 1) * 2;
                y = 5;
            }
            _items.Add(id, CollectPlannerTests.Leaf(id, state, "物品") with
            {
                Width = width,
                Height = height,
                Position = new GridPosition(x, y, false),
            });
            _owners.Add(id, owner);
            _grids.Add(id, 0);
        }

        public string Owner(string id) => _owners[id];
        public GridPosition Position(string id) => _items[id].Position!;
        public List<string> Contents(string id) =>
            _owners.Where(p => p.Value == id).Select(p => p.Key).ToList();
        public ItemSnapshot ReadSnapshot()
        {
            SnapshotReads++;
            return Build("root");
        }

        private ItemSnapshot Build(string id) => _items[id] with
        {
            Grids = _items[id].Grids.Select(g => g with
            {
                Items = Contents(id).Where(i => _grids[i] == g.Index)
                    .Select(Build).ToArray(),
            }).ToArray(),
        };

        public bool CanMoveToGrid(string itemId, string containerId, int gridIndex) =>
            !Denied.Contains(itemId) && !GridDenied.Contains(
                (itemId, containerId, gridIndex));

        public Task<PortResult> MoveToAsync(
            string containerId, int gridIndex, Placement placement)
        {
            Assert.Equal("root", Owner(placement.Id));
            Assert.Equal(LockState.Free, _items[placement.Id].Lock);
            MoveAttempts++;
            if (containerId == FailingMove)
            {
                return Task.FromResult(PortResult.Fail(MoveError));
            }
            _owners[placement.Id] = containerId;
            _grids[placement.Id] = gridIndex;
            SetPosition(placement);
            AssertValid();
            Events.Add($"move {placement.Id} -> {containerId}");
            return Task.FromResult(PortResult.Ok);
        }

        public Task<PortResult> ArrangeAsync(
            string containerId, int gridIndex, IReadOnlyList<Placement> placements)
        {
            if (containerId == FailingArrange
                && (FailingGrid == null || FailingGrid == gridIndex))
            {
                return Task.FromResult(PortResult.Fail("模拟失败"));
            }
            foreach (Placement placement in placements)
            {
                Assert.Equal(containerId, Owner(placement.Id));
                Assert.Equal(gridIndex, _grids[placement.Id]);
                Assert.Equal(LockState.Free, _items[placement.Id].Lock);
                SetPosition(placement);
            }
            AssertValid();
            Events.Add($"arrange {containerId}");
            return Task.FromResult(PortResult.Ok);
        }

        private void SetPosition(Placement p) => _items[p.Id] = _items[p.Id] with
        {
            Position = new GridPosition(p.X, p.Y, p.Rotated),
        };

        public void AssertValid()
        {
            foreach (GridSnapshot grid in _items.Values
                .Where(i => i.Grids.Count > 0).SelectMany(i => Build(i.Id).Grids))
            {
                var request = new PackRequest(grid.Width, grid.Height,
                    Array.Empty<FixedBlock>(), grid.Items.Select(i =>
                        new PackItem(i.Id, i.TemplateId, i.Width, i.Height)).ToArray());
                var result = new PackResult(grid.Items.Select(i => new Placement(
                    i.Id, i.Position!.X, i.Position.Y, i.Position.Rotated)).ToArray(),
                    Array.Empty<PackItem>());
                CpSatPackerTests.AssertValid(request, result);
            }
        }

        public IReadOnlyList<StackSnapshot> ReadStacks(string containerId)
        {
            StackReads++;
            return Array.Empty<StackSnapshot>();
        }
        public bool CanStack(string sourceId, string targetId) => false;
        public Task<StackMergeResult> MergeAsync(string sourceId, string targetId) =>
            throw new InvalidOperationException("测试没有堆叠");
        public Task<PortResult> FoldAsync(string itemId) =>
            throw new InvalidOperationException("测试没有折叠");
    }
}
