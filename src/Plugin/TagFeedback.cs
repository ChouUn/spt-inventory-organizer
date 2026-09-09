using System.Collections.Generic;
using System.Linq;
using ChouUn.InventoryOrganizer.Core.Tags;
using EFT.Communications;

namespace ChouUn.InventoryOrganizer;

/// <summary>把 tag 解析结果变成游戏内通知。</summary>
internal static class TagFeedback
{
    public static void Report(string tagText)
    {
        TagParseResult result = TagParser.Parse(tagText);
        if (!result.IsValid)
        {
            NotificationManager.DisplayWarningNotification(
                DescribeError(result.Error!), ENotificationDurationType.Long);
            return;
        }
        if (result.Rules.Count == 0)
        {
            return;
        }
        NotificationManager.DisplayMessageNotification(
            DescribeRules(result.Rules), ENotificationDurationType.Long);
    }

    private static string DescribeError(TagError error)
    {
        string head = $"第 {error.RuleIndex} 条规则{error.Message}";
        return $"{head}，该 tag 不参与整理：\n{error.Fragment}";
    }

    private static string DescribeRules(IReadOnlyList<TagRule> rules)
    {
        IEnumerable<string> lines = rules.Select(
            rule => $"规则 {rule.Index}：{RuleDescriber.Describe(rule)}");
        return "整理规则：\n" + string.Join("\n", lines);
    }
}
