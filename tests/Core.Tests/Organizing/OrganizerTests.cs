using System.Collections.Generic;
using System.Threading.Tasks;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Organizing;
using Xunit;

namespace ChouUn.InventoryOrganizer.Core.Tests.Organizing;

public sealed class OrganizerTests
{
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

        OrganizeReport report = await new Organizer(port).RunAsync();

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

        OrganizeReport report = await new Organizer(port).RunAsync();

        Assert.Equal(
            new[] { "mag1->small", "mag2->small", "mag2->big" },
            port.Moves);
        Assert.Equal(2, report.Moved);
        Assert.Equal(new[] { "容器 bad 的 tag 无效，未参与收纳" }, report.Warnings);
    }

    private sealed class FakePort : IInventoryPort
    {
        public ItemSnapshot Snapshot { get; set; } = null!;

        public string? FailingFold { get; set; }

        public HashSet<(string Item, string Container)> Full { get; } = new();

        public List<string> Folded { get; } = new List<string>();

        public List<string> Moves { get; } = new List<string>();

        public ItemSnapshot ReadSnapshot() => Snapshot;

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
    }
}
