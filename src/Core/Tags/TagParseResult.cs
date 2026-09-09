using System.Collections.Generic;

namespace ChouUn.InventoryOrganizer.Core.Tags;

/// <summary>解析错误。<see cref="Fragment"/> 是出错位置附近的原文。</summary>
public sealed record TagError(int RuleIndex, string Message, string Fragment);

/// <summary>
/// 有错误时整个 tag 的规则都不生效，<see cref="Rules"/> 只含出错前解析出的部分。
/// </summary>
public sealed record TagParseResult(IReadOnlyList<TagRule> Rules, TagError? Error)
{
    public bool IsValid => Error is null;
}
