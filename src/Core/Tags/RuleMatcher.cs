using System;
using System.Linq;
using ChouUn.InventoryOrganizer.Core.Inventory;

namespace ChouUn.InventoryOrganizer.Core.Tags;

/// <summary>按 docs/feats/tag-grammar.md 的原子语义判断物品是否匹配规则。</summary>
public static class RuleMatcher
{
    /// <summary>兜底规则匹配一切，容器是否接受由游戏判定。</summary>
    public static bool Matches(TagRule rule, ItemSnapshot item)
    {
        return rule.Expression is null || Matches(rule.Expression, item);
    }

    public static bool Matches(RuleExpression expression, ItemSnapshot item)
    {
        return expression.Alternatives.Any(
            conjunction => conjunction.Atoms.All(atom => Matches(atom, item)));
    }

    public static bool Matches(RuleAtom atom, ItemSnapshot item)
    {
        bool hit = atom.Kind == RuleAtomKind.Category
            ? item.Categories.Any(category => EqualsIgnoreCase(category, atom.Text))
            : Contains(item.Name, atom.Text) || Contains(item.ShortName, atom.Text);
        return hit != atom.Negated;
    }

    private static bool EqualsIgnoreCase(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string text, string part)
    {
        return text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
