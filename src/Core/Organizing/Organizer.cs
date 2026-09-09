using System.Collections.Generic;
using System.Threading.Tasks;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Tags;

namespace ChouUn.InventoryOrganizer.Core.Organizing;

/// <summary>按固定顺序驱动整理：折叠，然后收纳。每个阶段开始前重新读快照。</summary>
public sealed class Organizer
{
    private readonly IInventoryPort _port;

    public Organizer(IInventoryPort port)
    {
        _port = port;
    }

    public async Task<OrganizeReport> RunAsync()
    {
        var report = new OrganizeReport();
        await FoldAsync(report);
        await CollectAsync(report);
        return report;
    }

    private async Task FoldAsync(OrganizeReport report)
    {
        foreach (ItemSnapshot item in FoldPlanner.Plan(_port.ReadSnapshot()))
        {
            PortResult result = await _port.FoldAsync(item.Id);
            if (result.Succeeded)
            {
                report.Folded++;
            }
            else
            {
                report.Failures.Add($"折叠 {item.Name}：{result.Error}");
            }
        }
    }

    /// <summary>
    /// 规则按优先级逐条执行，每条规则把仍未收走且匹配的候选依次试着移入自己的容器。
    /// 放不进去的物品留给后面的规则；全部规则试完仍在的物品留在原地。
    /// 这里贪心地用游戏找到的第一个空位，容器内的碎片留给排布阶段处理。
    /// </summary>
    private async Task CollectAsync(OrganizeReport report)
    {
        ItemSnapshot root = _port.ReadSnapshot();
        var invalid = new List<string>();
        IReadOnlyList<Destination> destinations =
            CollectPlanner.Destinations(root, invalid);
        foreach (string container in invalid)
        {
            report.Warnings.Add($"容器 {container} 的 tag 无效，未参与收纳");
        }

        IReadOnlyList<ItemSnapshot> remaining = CollectPlanner.Candidates(root);
        foreach (Destination destination in destinations)
        {
            var leftovers = new List<ItemSnapshot>();
            foreach (ItemSnapshot item in remaining)
            {
                bool moved = RuleMatcher.Matches(destination.Rule, item)
                    && await TryMoveAsync(item, destination.Container);
                if (moved)
                {
                    report.Moved++;
                }
                else
                {
                    leftovers.Add(item);
                }
            }
            remaining = leftovers;
        }
    }

    private async Task<bool> TryMoveAsync(ItemSnapshot item, ItemSnapshot container)
    {
        PortResult result = await _port.MoveAsync(item.Id, container.Id);
        return result.Succeeded;
    }
}

/// <summary>一次整理的结果：各阶段成功计数、警告与逐项失败原因。</summary>
public sealed class OrganizeReport
{
    public int Folded { get; set; }

    public int Moved { get; set; }

    public List<string> Warnings { get; } = new List<string>();

    public List<string> Failures { get; } = new List<string>();
}
