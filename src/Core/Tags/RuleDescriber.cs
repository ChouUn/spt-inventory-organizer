using System.Linq;

namespace ChouUn.InventoryOrganizer.Core.Tags;

/// <summary>把规则复述成人可读的文字，用于保存 tag 时的成功提示。</summary>
public static class RuleDescriber
{
    public static string Describe(TagRule rule)
    {
        string prefix = rule.Order is int order ? $"#{order}：" : string.Empty;
        return prefix + (rule.Expression is null ? "兜底" : Describe(rule.Expression));
    }

    private static string Describe(RuleExpression expression)
    {
        return string.Join("，或 ", expression.Alternatives.Select(Describe));
    }

    private static string Describe(RuleConjunction conjunction)
    {
        return string.Join(" 且 ", conjunction.Atoms.Select(Describe));
    }

    private static string Describe(RuleAtom atom)
    {
        if (atom.Kind == RuleAtomKind.Category)
        {
            return atom.Negated ? $"类别非 {atom.Text}" : $"类别 {atom.Text}";
        }
        return atom.Negated ? $"名称不含 {atom.Text}" : $"名称含 {atom.Text}";
    }
}
