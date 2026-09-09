using System.Collections.Generic;
using System.Threading.Tasks;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Organizing;
using Xunit;

namespace ChouUn.InventoryOrganizer.Core.Tests.Organizing;

public sealed class OrganizerTests
{
    [Fact]
    public async Task 逐项折叠并汇总成功与失败()
    {
        var port = new FakePort { FailingId = "b" };
        ItemSnapshot root = FoldPlannerTests.Item(
            "root", false, false, LockState.Free,
            new GridSnapshot(0, 5, 5, new[]
            {
                FoldPlannerTests.Item("a", true, false, LockState.Free),
                FoldPlannerTests.Item("b", true, false, LockState.Free),
                FoldPlannerTests.Item("c", false, false, LockState.Free),
            }));

        OrganizeReport report = await new Organizer(port).RunAsync(root);

        Assert.Equal(new[] { "a", "b" }, port.Folded);
        Assert.Equal(1, report.Folded);
        Assert.Equal(new[] { "折叠 b：no room" }, report.Failures);
    }

    private sealed class FakePort : IInventoryPort
    {
        public List<string> Folded { get; } = new List<string>();

        public string? FailingId { get; set; }

        public Task<PortResult> FoldAsync(string itemId)
        {
            Folded.Add(itemId);
            PortResult result = itemId == FailingId
                ? PortResult.Fail("no room")
                : PortResult.Ok;
            return Task.FromResult(result);
        }
    }
}
