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
    /// 规则按优先级逐条执行，先补充堆叠，再联合排布原有物品与本规则候选。
    /// 原有物品必留；未选中或游戏拒绝的候选留给后面的规则。
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

        var remaining = new HashSet<string>(
            CollectPlanner.Candidates(root).Select(i => i.Id));
        foreach (Destination destination in destinations)
        {
            ItemSnapshot[] matching = CollectPlanner.Candidates(root)
                .Where(i => remaining.Contains(i.Id)
                    && RuleMatcher.Matches(destination.Rule, i)).ToArray();
            if (matching.Length == 0)
            {
                continue;
            }
            foreach (ItemSnapshot item in matching)
            {
                if (await _merger.TopUpAsync(
                    root.Id, item.Id, destination.Container.Id, report))
                {
                    report.Moved++;
                    remaining.Remove(item.Id);
                }
            }
            await CollectIntoAsync(destination, matching, remaining, report);
            root = _port.ReadSnapshot();
        }
    }

    /// <summary>每个网格先给已选候选留出空位，再逐项走游戏移动事务。</summary>
    private async Task CollectIntoAsync(
        Destination destination, IReadOnlyList<ItemSnapshot> matching,
        HashSet<string> remaining, OrganizeReport report)
    {
        foreach (GridSnapshot originalGrid in destination.Container.Grids)
        {
            if (!matching.Any(i => remaining.Contains(i.Id)))
            {
                break;
            }
            ItemSnapshot root = _port.ReadSnapshot();
            ItemSnapshot? container = OrganizeScope.Containers(root)
                .FirstOrDefault(c => c.Id == destination.Container.Id);
            if (container is null)
            {
                return;
            }
            GridSnapshot grid = container.Grids
                .Single(g => g.Index == originalGrid.Index);
            ItemSnapshot[] candidates = matching.Where(i => remaining.Contains(i.Id)
                && _port.CanMoveToGrid(i.Id, container.Id, grid.Index)).ToArray();
            if (candidates.Length == 0)
            {
                continue;
            }
            GridPackJob job = PackPlanner.ForGrid(container, grid);
            PackRequest request = job.Request with
            {
                Items = job.Request.Items.Concat(candidates.Select(i =>
                    new PackItem(i.Id, i.TemplateId, i.Width, i.Height))).ToArray(),
            };
            PackResult packed = await SolveAsync(request, container.Name, report);
            var candidateIds = new HashSet<string>(candidates.Select(i => i.Id));
            Placement[] incoming = packed.Placements
                .Where(p => candidateIds.Contains(p.Id)).ToArray();
            if (incoming.Length == 0 || packed.Unplaced.Any(i => i.Required))
            {
                continue;
            }
            Placement[] existing = packed.Placements
                .Where(p => !candidateIds.Contains(p.Id)).ToArray();
            if (!await ApplyAsync(job, Changed(job, existing), report))
            {
                continue;
            }
            bool movedIntoGrid = false;
            foreach (Placement placement in incoming)
            {
                // 前一个候选刚移入时可能新增可补充堆叠，重新询问当前数量。
                bool consumed = await _merger.TopUpAsync(
                    root.Id, placement.Id, container.Id, report);
                if (!consumed)
                {
                    PortResult moved = await _port.MoveToAsync(
                        container.Id, grid.Index, placement);
                    if (!moved.Succeeded)
                    {
                        ItemSnapshot item = candidates
                            .Single(i => i.Id == placement.Id);
                        report.Failures.Add(
                            $"收纳 {item.Name} ({item.Id}) -> " +
                            $"{container.Name} ({container.Id}) 网格 {grid.Index}：" +
                            moved.Error);
                        continue;
                    }
                    movedIntoGrid = true;
                }
                report.Moved++;
                remaining.Remove(placement.Id);
            }
            if (movedIntoGrid)
            {
                // 新移入的堆叠可能还有容量。先用本规则所有剩余候选补充，
                // 包括没有被选入布局的物品，再把余量留给下一网格或后续规则。
                foreach (ItemSnapshot item in matching)
                {
                    if (remaining.Contains(item.Id) && await _merger.TopUpAsync(
                        root.Id, item.Id, container.Id, report))
                    {
                        report.Moved++;
                        remaining.Remove(item.Id);
                    }
                }
            }
        }
    }

    /// <summary>逐网格排布。放不下全部物品或布局与现状相同的网格不动。</summary>
    private async Task PackAsync(OrganizeReport report)
    {
        ItemSnapshot root = _port.ReadSnapshot();
        IReadOnlyList<GridPackJob> jobs = PackPlanner.Plan(root);
        foreach (GridPackJob job in jobs)
        {
            string where = $"{job.Container.Name} 网格 {job.GridIndex}";
            PackResult packed = await SolveAsync(job.Request, where, report);
            if (!packed.Complete)
            {
                report.Warnings.Add(
                    $"{where} 排布放不下 {packed.Unplaced.Count} 件，保持原样");
                continue;
            }
            await ApplyAsync(job, Changed(job, packed.Placements), report);
        }
    }

    /// <summary>收纳与排布共用 3 秒预算，单次最多 1 秒；纯数据在后台求解。</summary>
    private async Task<PackResult> SolveAsync(
        PackRequest request, string where, OrganizeReport report)
    {
        double seconds = System.Math.Max(0,
            System.Math.Min(1, 3 - report.PackPlanningMilliseconds / 1000.0));
        var elapsed = Stopwatch.StartNew();
        PackResult result = await Task.Run(() => _packer.Pack(request, seconds));
        report.PackPlanningMilliseconds += elapsed.ElapsedMilliseconds;
        report.Diagnostics.Add($"packing {where}: {result.Diagnostic}");
        if (result.Warning != null)
        {
            report.Warnings.Add($"{where}：{result.Warning}");
        }
        return result;
    }

    private async Task<bool> ApplyAsync(
        GridPackJob job, IReadOnlyList<Placement> changed, OrganizeReport report)
    {
        if (changed.Count == 0)
        {
            return true;
        }
        PortResult result = await _port.ArrangeAsync(
            job.Container.Id, job.GridIndex, changed);
        if (result.Succeeded)
        {
            report.Packed++;
        }
        else
        {
            report.Failures.Add($"排布 {job.Container.Name}：{result.Error}");
        }
        return result.Succeeded;
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

    public List<string> Diagnostics { get; } = new List<string>();
}
