using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Packing;
using ChouUn.InventoryOrganizer.Core.Tags;

namespace ChouUn.InventoryOrganizer.Core.Organizing;

/// <summary>按固定顺序驱动折叠、合并、收纳、排布，每个阶段前读取最新快照。</summary>
public sealed class Organizer
{
    private readonly IInventoryPort _port;
    private readonly IPacker _packer;
    private readonly StackMerger _merger;

    public Organizer(IInventoryPort port, IPacker packer)
    {
        _port = port;
        _packer = packer;
        _merger = new StackMerger(port);
    }

    public async Task<OrganizeReport> RunAsync()
    {
        var report = new OrganizeReport();
        await FoldAsync(report);
        await _merger.MergeContainersAsync(_port.ReadSnapshot(), report);
        await CollectAsync(report);
        await PackAsync(report);
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
    /// 规则按优先级逐条执行，匹配后先补充已有堆叠，再尝试移入剩余物品。
    /// 放不进去的剩余数量留给后面的规则；全部规则试完仍在的物品留在原地。
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
                    && await TryMoveAsync(root.Id, item, destination.Container, report);
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

    private async Task<bool> TryMoveAsync(
        string rootId, ItemSnapshot item, ItemSnapshot container, OrganizeReport report)
    {
        if (await _merger.TopUpAsync(rootId, item.Id, container.Id, report))
        {
            return true;
        }
        PortResult result = await _port.MoveAsync(item.Id, container.Id);
        return result.Succeeded;
    }

    /// <summary>逐网格排布。放不下全部物品或布局与现状相同的网格不动。</summary>
    private async Task PackAsync(OrganizeReport report)
    {
        ItemSnapshot root = _port.ReadSnapshot();
        var planning = Stopwatch.StartNew();
        IReadOnlyList<GridPackJob> jobs = PackPlanner.Plan(root);
        planning.Stop();
        foreach (GridPackJob job in jobs)
        {
            string where = $"{job.Container.Name} 网格 {job.GridIndex}";
            planning.Start();
            PackResult packed = _packer.Pack(job.Request);
            planning.Stop();
            if (!packed.Complete)
            {
                report.Warnings.Add(
                    $"{where} 排布放不下 {packed.Unplaced.Count} 件，保持原样");
                continue;
            }
            IReadOnlyList<Placement> changed = Changed(job, packed.Placements);
            if (changed.Count == 0)
            {
                continue;
            }
            PortResult result = await _port.ArrangeAsync(
                job.Container.Id, job.GridIndex, changed);
            if (result.Succeeded)
            {
                report.Packed++;
            }
            else
            {
                report.Failures.Add($"排布 {where}：{result.Error}");
            }
        }
        report.PackPlanningMilliseconds = planning.ElapsedMilliseconds;
    }

    /// <summary>
    /// 只提交位置有变化的物品。未变的物品留在原位当障碍，布局本身保证二者不重叠；
    /// 少动物品就少触发界面刷新，具体耗时由适配层诊断日志测量。
    /// </summary>
    private static IReadOnlyList<Placement> Changed(
        GridPackJob job, IReadOnlyList<Placement> placements)
    {
        Dictionary<string, GridPosition?> current =
            job.FreeItems.ToDictionary(item => item.Id, item => item.Position);
        return placements.Where(p =>
            !(current.TryGetValue(p.Id, out GridPosition? position)
              && position is GridPosition g
              && g.X == p.X && g.Y == p.Y && g.Rotated == p.Rotated)).ToList();
    }
}

/// <summary>一次整理的结果：各阶段成功计数、警告与逐项失败原因。</summary>
public sealed class OrganizeReport
{
    public int Folded { get; set; }

    public int Moved { get; set; }

    public int Packed { get; set; }

    public int Merged { get; set; }

    public long MergeMilliseconds { get; set; }

    public long PackPlanningMilliseconds { get; set; }

    public List<string> Warnings { get; } = new List<string>();

    public List<string> Failures { get; } = new List<string>();
}
