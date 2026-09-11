using System.Collections.Generic;

namespace ChouUn.StashMaster.Core.Tags;

/// <summary>
/// 一条 <c>@o[#order] [expr;]</c> 规则。<see cref="Expression"/> 为 null 表示兜底。
/// <see cref="Index"/> 是该规则在 tag 里的序号，从 1 起，供提示信息引用。
/// </summary>
public sealed record TagRule(int Index, int? Order, RuleExpression? Expression)
{
    public bool IsCatchAll => Expression is null;
}

/// <summary>析取范式：任一 <see cref="Alternatives"/> 成立即匹配。</summary>
public sealed record RuleExpression(IReadOnlyList<RuleConjunction> Alternatives);

/// <summary>合取项：全部 <see cref="Atoms"/> 成立才成立。</summary>
public sealed record RuleConjunction(IReadOnlyList<RuleAtom> Atoms);

public enum RuleAtomKind
{
    /// <summary>类别链任一级显示名相等。</summary>
    Category,

    /// <summary>显示名或短名包含。</summary>
    Name,
}

public sealed record RuleAtom(RuleAtomKind Kind, string Text, bool Negated);
