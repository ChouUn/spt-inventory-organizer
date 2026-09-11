using System.Collections.Generic;
using System.Linq;
using ChouUn.StashMaster.Core.Inventory;
using ChouUn.StashMaster.Core.Tags;

namespace ChouUn.StashMaster.Core.Organizing;

/// <summary>一个目的地：带规则的容器加它的一条规则。</summary>
public sealed record Destination(ItemSnapshot Container, TagRule Rule);

/// <summary>
/// 收纳阶段的静态部分。目的地是根容器之内任意深度带有效规则的容器；
/// 候选是根容器网格里散放、未锁、本身不带规则的物品。
/// 未打 tag 的容器内部不动，这与用户对「整理仓库」的预期一致，也避免拆散已有的包。
/// </summary>
public static class CollectPlanner
{
    /// <summary>
    /// 按优先级排好的目的地：数字小的先，未写的最后，其余保持遍历顺序。
    /// </summary>
    public static IReadOnlyList<Destination> Destinations(
        ItemSnapshot root, ICollection<string> invalidTagContainers,
        System.Func<string, TagParseResult>? parse = null)
    {
        var destinations = new List<Destination>();
        foreach (ItemSnapshot container in Descendants(root))
        {
            if (container.Tag is null)
            {
                continue;
            }
            TagParseResult parsed = (parse ?? TagParser.Parse)(container.Tag);
            if (!parsed.IsValid)
            {
                invalidTagContainers.Add(container.Name);
                continue;
            }
            destinations.AddRange(
                parsed.Rules.Select(rule => new Destination(container, rule)));
        }
        return destinations.OrderBy(d => d.Rule.Order ?? int.MaxValue).ToList();
    }

    public static IReadOnlyList<ItemSnapshot> Candidates(ItemSnapshot root)
    {
        return root.Grids
            .SelectMany(grid => grid.Items)
            .Where(item => item.Lock == LockState.Free && !HasRuleHead(item))
            .ToList();
    }

    /// <summary>
    /// 带 tag 规则头的容器是目的地，不作候选；tag 写错了也一样，避免把它挪走。
    /// </summary>
    private static bool HasRuleHead(ItemSnapshot item)
    {
        return item.Tag != null && item.Tag.Contains(TagParser.RuleHead);
    }

    private static IEnumerable<ItemSnapshot> Descendants(ItemSnapshot item)
    {
        // Locked 子树不作为收纳目的地，也不发起向内部堆叠补充数量的尝试。
        if (item.Lock == LockState.Locked)
        {
            yield break;
        }
        foreach (GridSnapshot grid in item.Grids)
        {
            foreach (ItemSnapshot child in grid.Items)
            {
                if (child.Lock == LockState.Locked)
                {
                    continue;
                }
                yield return child;
                foreach (ItemSnapshot deeper in Descendants(child))
                {
                    yield return deeper;
                }
            }
        }
    }
}
