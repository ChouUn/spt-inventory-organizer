using System.Collections.Generic;
using System.Linq;
using ChouUn.StashMaster.Core.Inventory;
using ChouUn.StashMaster.Core.Tags;

namespace ChouUn.StashMaster.Core.Organizing;

/// <summary>容器内整理的共同范围：根与带有效规则的子容器，跳过 Locked 子树。</summary>
public static class OrganizeScope
{
    public static IEnumerable<ItemSnapshot> Containers(ItemSnapshot root,
        System.Func<string, TagParseResult>? parse = null)
    {
        if (root.Lock == LockState.Locked)
        {
            yield break;
        }
        yield return root;
        foreach (ItemSnapshot container in TaggedDescendants(
            root, parse ?? TagParser.Parse))
        {
            yield return container;
        }
    }

    private static IEnumerable<ItemSnapshot> TaggedDescendants(ItemSnapshot container,
        System.Func<string, TagParseResult> parse)
    {
        foreach (ItemSnapshot child in container.Grids.SelectMany(grid => grid.Items))
        {
            if (child.Lock == LockState.Locked)
            {
                continue;
            }
            if (child.Tag != null)
            {
                TagParseResult parsed = parse(child.Tag);
                if (parsed.IsValid && parsed.Rules.Count > 0)
                {
                    yield return child;
                }
            }
            foreach (ItemSnapshot nested in TaggedDescendants(child, parse))
            {
                yield return nested;
            }
        }
    }
}
