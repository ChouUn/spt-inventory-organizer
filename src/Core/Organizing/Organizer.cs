using System.Collections.Generic;
using System.Threading.Tasks;
using ChouUn.InventoryOrganizer.Core.Inventory;

namespace ChouUn.InventoryOrganizer.Core.Organizing;

/// <summary>按固定顺序驱动整理的各阶段。目前只有折叠阶段。</summary>
public sealed class Organizer
{
    private readonly IInventoryPort _port;

    public Organizer(IInventoryPort port)
    {
        _port = port;
    }

    public async Task<OrganizeReport> RunAsync(ItemSnapshot root)
    {
        var report = new OrganizeReport();
        foreach (ItemSnapshot item in FoldPlanner.Plan(root))
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
        return report;
    }
}

/// <summary>一次整理的结果：成功计数与逐项失败原因。</summary>
public sealed class OrganizeReport
{
    public int Folded { get; set; }

    public List<string> Failures { get; } = new List<string>();
}
