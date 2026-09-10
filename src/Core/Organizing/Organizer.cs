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
    private readonly ContainerPacker _containerPacker;
    private PackBudget _budget = new();
    private readonly Dictionary<string, TagParseResult> _tags = new();

    public Organizer(IInventoryPort port, IPacker packer)
    {
        _port = port;
        _packer = packer;
        _merger = new StackMerger(port);
        _containerPacker = new ContainerPacker(packer);
    }

    public async Task<OrganizeReport> RunAsync()
    {
        var report = new OrganizeReport();
        _budget = new PackBudget();
        _tags.Clear();
        await FoldAsync(report);
        await _merger.MergeContainersAsync(ReadSnapshot(report), report, ParseTag);
        await CollectAsync(report);
        await PackAsync(report);
        report.Diagnostics.Add(
            $"work: snapshots={report.SnapshotReads}, stacks={report.StackReads}, " +
            $"stack-checks={report.StackChecks}, tag-parses={_tags.Count}, " +
            $"condition-matches={report.RuleMatches}; " +
            $"budget-used: collection={_budget.CollectionSeconds:F3}s, " +
            $"final={_budget.FinalSeconds:F3}s");
        return report;
    }

    /// <summary>同一整理内相同 tag 只解析一次，各阶段共用解析结果。</summary>
    private TagParseResult ParseTag(string text)
    {
        if (!_tags.TryGetValue(text, out TagParseResult? parsed))
        {
            _tags.Add(text, parsed = TagParser.Parse(text));
        }
        return parsed;
    }

    private ItemSnapshot ReadSnapshot(OrganizeReport report)
    {
        report.SnapshotReads++;
        return _port.ReadSnapshot();
    }

    private async Task FoldAsync(OrganizeReport report)
    {
        foreach (ItemSnapshot item in FoldPlanner.Plan(ReadSnapshot(report)))
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
        ItemSnapshot root = ReadSnapshot(report);
        var state = new CollectionState(root);
        var invalid = new List<string>();
        IReadOnlyList<Destination> destinations =
            CollectPlanner.Destinations(root, invalid, ParseTag);
        foreach (string container in invalid)
        {
            report.Warnings.Add($"容器 {container} 的 tag 无效，未参与收纳");
        }

        IReadOnlyList<ItemSnapshot> candidates = CollectPlanner.Candidates(root);
        var remaining = new HashSet<string>(candidates.Select(i => i.Id));
        var matches = new Dictionary<(RuleAtom Atom, string Item), bool>();
        foreach (Destination destination in destinations)
        {
            ItemSnapshot[] matching = candidates.Where(i => remaining.Contains(i.Id))
                .Where(i => Matches(destination.Rule, i, matches, report)).ToArray();
            if (matching.Length == 0)
            {
                continue;
            }
            int failures = report.Failures.Count;
            foreach (ItemSnapshot item in matching)
            {
                if (await _merger.TopUpAsync(
                    item.Id, destination.Container.Id, report))
                {
                    report.Moved++;
                    remaining.Remove(item.Id);
                }
            }
            state.Remove(_merger.Consumed);
            await CollectIntoAsync(destination, matching, remaining, state, report);
            // 普通移动与合并通过结果更新索引；失败或移动容器时刷新可能受影响的层级。
            if (report.Failures.Count != failures
                || matching.Any(i => i.Grids.Count > 0 && !remaining.Contains(i.Id)))
            {
                root = ReadSnapshot(report);
                state = new CollectionState(root);
                candidates = CollectPlanner.Candidates(root);
                remaining.IntersectWith(candidates.Select(i => i.Id));
                matches.Clear();
            }
        }
    }

    /// <summary>相同条件对同一物品只求值一次，状态校准后清空缓存。</summary>
    private static bool Matches(TagRule rule, ItemSnapshot item,
        Dictionary<(RuleAtom Atom, string Item), bool> cache, OrganizeReport report)
    {
        return rule.Expression is null || rule.Expression.Alternatives.Any(term =>
            term.Atoms.All(atom =>
            {
                var key = (atom, item.Id);
                if (!cache.TryGetValue(key, out bool matches))
                {
                    report.RuleMatches++;
                    cache.Add(key, matches = RuleMatcher.Matches(atom, item));
                }
                return matches;
            }));
    }

    /// <summary>一次联合选择全部网格的候选，再逐网格腾位并提交移动。</summary>
    private async Task CollectIntoAsync(
        Destination destination, IReadOnlyList<ItemSnapshot> matching,
        HashSet<string> remaining, CollectionState state, OrganizeReport report)
    {
        if (!matching.Any(i => remaining.Contains(i.Id)))
        {
            return;
        }
        ItemSnapshot container = state.Container(destination.Container.Id);
        GridPackJob[] jobs = container.Grids
            .Select(g => PackPlanner.ForGrid(container, g)).ToArray();
        var candidates = matching.Where(i => remaining.Contains(i.Id))
            .ToDictionary(i => i.Id);
        PackRequest[] requests = jobs.Select(job => job.Request with
        {
            Items = job.Request.Items.Concat(candidates.Values
                .Where(i => _port.CanMoveToGrid(i.Id, container.Id, job.GridIndex))
                .Select(i => new PackItem(i.Id, i.TemplateId, i.Width, i.Height)
                {
                    CategoryPath = i.CategoryPath,
                }))
                .ToArray(),
        }).ToArray();
        if (!requests.Any(r => r.Items.Any(i => !i.Required)))
        {
            return;
        }
        double seconds = _budget.Available(true);
        var elapsed = Stopwatch.StartNew();
        ContainerPackResult result = await Task.Run(() =>
            _containerPacker.Pack(requests, seconds));
        RecordSolve(true, elapsed.Elapsed.TotalSeconds, container.Name,
            result.Diagnostic, result.Warning, report);
        for (int index = 0; index < jobs.Length; index++)
        {
            GridPackJob job = jobs[index];
            PackResult packed = result.Grids[index];
            Placement[] incoming = packed.Placements
                .Where(p => candidates.ContainsKey(p.Id) && remaining.Contains(p.Id))
                .ToArray();
            if (incoming.Length == 0 || packed.Unplaced.Any(i => i.Required))
            {
                continue;
            }
            Placement[] existing = packed.Placements
                .Where(p => !candidates.ContainsKey(p.Id)).ToArray();
            if (!await ApplyAsync(job, Changed(job, existing), report))
            {
                continue;
            }
            state.Arrange(existing);
            bool movedIntoGrid = false;
            foreach (Placement placement in incoming)
            {
                // 前一个候选刚移入时可能新增可补充堆叠，重新询问当前数量。
                bool consumed = await _merger.TopUpAsync(
                    placement.Id, container.Id, report);
                if (!consumed)
                {
                    PortResult moved = await _port.MoveToAsync(
                        container.Id, job.GridIndex, placement);
                    if (!moved.Succeeded)
                    {
                        ItemSnapshot item = candidates[placement.Id];
                        report.Failures.Add(
                            $"收纳 {item.Name} ({item.Id}) -> " +
                            $"{container.Name} ({container.Id}) " +
                            $"网格 {job.GridIndex}：" +
                            moved.Error);
                        continue;
                    }
                    movedIntoGrid = true;
                    _merger.Moved(placement.Id, container.Id);
                    state.Move(container.Id, job.GridIndex, placement);
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
                        item.Id, container.Id, report))
                    {
                        report.Moved++;
                        remaining.Remove(item.Id);
                    }
                }
            }
            state.Remove(_merger.Consumed);
        }
    }

    /// <summary>逐网格排布。放不下全部物品或布局与现状相同的网格不动。</summary>
    private async Task PackAsync(OrganizeReport report)
    {
        ItemSnapshot root = ReadSnapshot(report);
        IReadOnlyList<GridPackJob> jobs = PackPlanner.Plan(root, ParseTag);
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
        double seconds = _budget.Available(false);
        var elapsed = Stopwatch.StartNew();
        PackResult result = await Task.Run(() => _packer.Pack(request, seconds));
        RecordSolve(false, elapsed.Elapsed.TotalSeconds, where,
            result.Diagnostic, result.Warning, report);
        return result;
    }

    private void RecordSolve(bool collecting, double seconds, string where,
        string diagnostic, string? warning, OrganizeReport report)
    {
        double allotted = _budget.Available(collecting);
        _budget.Charge(collecting, seconds);
        report.PackPlanningMilliseconds += (long)(seconds * 1000);
        string stage = collecting ? "collection" : "final";
        report.Diagnostics.Add($"packing {where}: {diagnostic}; " +
            $"stage={stage}, allotted={allotted:F3}s, elapsed={seconds:F3}s, " +
            $"stage-remaining={_budget.Remaining(collecting):F3}s");
        if (warning != null)
        {
            report.Warnings.Add($"{where}：{warning}");
        }
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
    public int StackReads { get; set; }

    public int StackChecks { get; set; }

    public int SnapshotReads { get; set; }

    public int RuleMatches { get; set; }

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
