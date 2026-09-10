using System.Collections.Generic;
using System.Linq;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Tags;

namespace ChouUn.InventoryOrganizer.Core.Organizing;

/// <summary>容器内整理的共同范围：根与带有效规则的子容器，跳过 Locked 子树。</summary>
public static class OrganizeScope
{
    public static IEnumerable<ItemSnapshot> Containers(ItemSnapshot root)
    {
        if (root.Lock == LockState.Locked)
        {
            yield break;
        }
        yield return root;
        foreach (ItemSnapshot container in TaggedDescendants(root))
        {
            yield return container;
        }
    }

    private static IEnumerable<ItemSnapshot> TaggedDescendants(ItemSnapshot container)
    {
        foreach (ItemSnapshot child in container.Grids.SelectMany(grid => grid.Items))
        {
            if (child.Lock == LockState.Locked)
            {
                continue;
            }
            if (child.Tag != null)
            {
                TagParseResult parsed = TagParser.Parse(child.Tag);
                if (parsed.IsValid && parsed.Rules.Count > 0)
                {
                    yield return child;
                }
            }
            foreach (ItemSnapshot nested in TaggedDescendants(child))
            {
                yield return nested;
            }
        }
    }
}
