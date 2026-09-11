using System;
using System.Collections.Generic;
using System.Globalization;

namespace ChouUn.StashMaster.Core.Tags;

/// <summary>按 docs/feats/tag-grammar.md 解析 tag 文本。</summary>
public static class TagParser
{
    public const string RuleHead = "@o";
    private const string NamePrefix = "n:";
    private const int FragmentLength = 24;
    private static readonly string[] OrSeparator = { "||" };
    private static readonly string[] AndSeparator = { "&&" };

    public static TagParseResult Parse(string text)
    {
        var rules = new List<TagRule>();
        int search = 0;
        while (true)
        {
            int start = text.IndexOf(RuleHead, search, StringComparison.Ordinal);
            if (start < 0)
            {
                return new TagParseResult(rules, null);
            }

            int index = rules.Count + 1;
            int pos = start + RuleHead.Length;

            int? order = null;
            if (pos < text.Length && text[pos] == '#')
            {
                int digitsStart = pos + 1;
                int digitsEnd = digitsStart;
                while (digitsEnd < text.Length && IsAsciiDigit(text[digitsEnd]))
                {
                    digitsEnd++;
                }
                string digits = text.Substring(digitsStart, digitsEnd - digitsStart);
                if (digits.Length == 0)
                {
                    return Fail(rules, index, "# 后必须是数字", text, start);
                }
                if (!int.TryParse(
                        digits,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int parsed))
                {
                    return Fail(rules, index, "优先级数字过大", text, start);
                }
                order = parsed;
                pos = digitsEnd;
            }

            int afterHead = pos;
            while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            {
                pos++;
            }

            // 裸规则：头之后是结尾、`;` 或下一条规则。
            if (pos >= text.Length || text[pos] == ';' || IsRuleHeadAt(text, pos))
            {
                rules.Add(new TagRule(index, order, null));
                search = pos < text.Length && text[pos] == ';' ? pos + 1 : pos;
                continue;
            }

            if (pos == afterHead)
            {
                return Fail(rules, index, "规则头后缺少空白", text, start);
            }

            int end = text.IndexOf(';', pos);
            int nextHead = text.IndexOf(RuleHead, pos, StringComparison.Ordinal);
            if (end < 0 || (nextHead >= 0 && nextHead < end))
            {
                return Fail(rules, index, "表达式未闭合，缺少结尾的 ;", text, start);
            }

            RuleExpression? expression = ParseExpression(
                text.Substring(pos, end - pos), index, out TagError? error);
            if (expression is null)
            {
                return new TagParseResult(rules, error);
            }
            rules.Add(new TagRule(index, order, expression));
            search = end + 1;
        }
    }

    private static RuleExpression? ParseExpression(
        string source, int ruleIndex, out TagError? error)
    {
        var alternatives = new List<RuleConjunction>();
        string[] alternativeTexts = source.Split(OrSeparator, StringSplitOptions.None);
        foreach (string alternative in alternativeTexts)
        {
            var atoms = new List<RuleAtom>();
            string[] factors = alternative.Split(AndSeparator, StringSplitOptions.None);
            foreach (string factor in factors)
            {
                string body = factor.Trim();
                bool negated = false;
                while (body.StartsWith("!", StringComparison.Ordinal))
                {
                    negated = !negated;
                    body = body.Substring(1).TrimStart();
                }
                if (body.Length == 0)
                {
                    error = new TagError(ruleIndex, "条件为空", source.Trim());
                    return null;
                }
                if (body.StartsWith(NamePrefix, StringComparison.Ordinal))
                {
                    string name = body.Substring(NamePrefix.Length).Trim();
                    if (name.Length == 0)
                    {
                        error = new TagError(ruleIndex, "名称为空", body);
                        return null;
                    }
                    atoms.Add(new RuleAtom(RuleAtomKind.Name, name, negated));
                }
                else
                {
                    atoms.Add(new RuleAtom(RuleAtomKind.Category, body, negated));
                }
            }
            alternatives.Add(new RuleConjunction(atoms));
        }
        error = null;
        return new RuleExpression(alternatives);
    }

    private static TagParseResult Fail(
        List<TagRule> rules, int ruleIndex, string message, string text, int start)
    {
        int length = Math.Min(FragmentLength, text.Length - start);
        string fragment = text.Substring(start, length);
        return new TagParseResult(rules, new TagError(ruleIndex, message, fragment));
    }

    private static bool IsRuleHeadAt(string text, int pos)
    {
        return string.CompareOrdinal(text, pos, RuleHead, 0, RuleHead.Length) == 0;
    }

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
}
