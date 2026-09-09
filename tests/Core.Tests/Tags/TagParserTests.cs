using System.Linq;
using ChouUn.InventoryOrganizer.Core.Tags;
using Xunit;

namespace ChouUn.InventoryOrganizer.Core.Tests.Tags;

public sealed class TagParserTests
{
    [Theory]
    [InlineData("@o", null)]
    [InlineData("@o;", null)]
    [InlineData("@o#3", 3)]
    [InlineData("@o#3;", 3)]
    [InlineData("@o#0", 0)]
    public void 裸规则是兜底(string text, int? order)
    {
        TagRule rule = Assert.Single(ParseValid(text));

        Assert.True(rule.IsCatchAll);
        Assert.Equal(order, rule.Order);
        Assert.Equal(1, rule.Index);
    }

    [Theory]
    [InlineData("@o ammo;", null)]
    [InlineData("@o#1 ammo;", 1)]
    [InlineData("@o#12   ammo ;", 12)]
    public void 单个类别原子(string text, int? order)
    {
        TagRule rule = Assert.Single(ParseValid(text));

        Assert.Equal(order, rule.Order);
        RuleAtom atom = SingleAtom(rule);
        Assert.Equal(RuleAtomKind.Category, atom.Kind);
        Assert.Equal("ammo", atom.Text);
        Assert.False(atom.Negated);
    }

    [Fact]
    public void 名称原子去掉前缀与空白()
    {
        RuleAtom atom = SingleAtom(Assert.Single(ParseValid("@o n: 5.45 ;")));

        Assert.Equal(RuleAtomKind.Name, atom.Kind);
        Assert.Equal("5.45", atom.Text);
    }

    [Fact]
    public void 与高于或()
    {
        TagRule rule = Assert.Single(ParseValid("@o n:ak&&n:74||magazines;"));

        Assert.Collection(
            rule.Expression!.Alternatives,
            first => Assert.Equal(new[] { "ak", "74" }, Texts(first)),
            second => Assert.Equal(new[] { "magazines" }, Texts(second)));
    }

    [Theory]
    [InlineData("@o ammo&&!n:5.45;", true)]
    [InlineData("@o ammo&&!!n:5.45;", false)]
    [InlineData("@o ammo&&! n:5.45;", true)]
    public void 取反作用于紧随的原子(string text, bool negated)
    {
        TagRule rule = Assert.Single(ParseValid(text));
        RuleAtom name = Assert.Single(rule.Expression!.Alternatives).Atoms[1];

        Assert.Equal(RuleAtomKind.Name, name.Kind);
        Assert.Equal(negated, name.Negated);
        Assert.False(rule.Expression.Alternatives[0].Atoms[0].Negated);
    }

    [Theory]
    [InlineData("@o Weapon parts & mods;", "Weapon parts & mods")]
    [InlineData("@o a|b;", "a|b")]
    [InlineData("@o n:B&T;", "B&T")]
    [InlineData("@o n:PACA (Rivals);", "PACA (Rivals)")]
    [InlineData("@o n:UAV SAS disk #1;", "UAV SAS disk #1")]
    [InlineData("@o n:BlackHawk! Commando;", "BlackHawk! Commando")]
    public void 非运算符字符是普通文本(string text, string expected)
    {
        Assert.Equal(expected, SingleAtom(Assert.Single(ParseValid(text))).Text);
    }

    [Fact]
    public void 多条规则与自由文本()
    {
        var rules = ParseValid("弹药 @o#1 n:5.45; 说明 @o#5 ammo; @o");

        Assert.Collection(
            rules,
            first =>
            {
                Assert.Equal(1, first.Index);
                Assert.Equal(1, first.Order);
                Assert.Equal("5.45", SingleAtom(first).Text);
            },
            second =>
            {
                Assert.Equal(2, second.Index);
                Assert.Equal(5, second.Order);
                Assert.Equal("ammo", SingleAtom(second).Text);
            },
            third =>
            {
                Assert.Equal(3, third.Index);
                Assert.True(third.IsCatchAll);
            });
    }

    [Theory]
    [InlineData("")]
    [InlineData("只是一个名字")]
    [InlineData("@O ammo;")]
    public void 没有规则头时没有规则(string text)
    {
        Assert.Empty(ParseValid(text));
    }

    [Theory]
    [InlineData("@o ammo", 1, "未闭合")]
    [InlineData("@o ammo @o meds;", 1, "未闭合")]
    [InlineData("@o 杂物", 1, "未闭合")]
    [InlineData("@o ammo||;", 1, "条件为空")]
    [InlineData("@o &&ammo;", 1, "条件为空")]
    [InlineData("@o !;", 1, "条件为空")]
    [InlineData("@o n:;", 1, "名称为空")]
    [InlineData("@o n: ;", 1, "名称为空")]
    [InlineData("@o#a ammo;", 1, "必须是数字")]
    [InlineData("@o# ammo;", 1, "必须是数字")]
    [InlineData("@o#99999999999 ammo;", 1, "过大")]
    [InlineData("@oammo;", 1, "缺少空白")]
    [InlineData("@o#1ammo;", 1, "缺少空白")]
    [InlineData("@o ammo; @o meds", 2, "未闭合")]
    public void 错误带规则序号(string text, int ruleIndex, string messagePart)
    {
        TagParseResult result = TagParser.Parse(text);

        Assert.False(result.IsValid);
        Assert.Equal(ruleIndex, result.Error!.RuleIndex);
        Assert.Contains(messagePart, result.Error.Message);
        Assert.NotEmpty(result.Error.Fragment);
    }

    private static TagRule[] ParseValid(string text)
    {
        TagParseResult result = TagParser.Parse(text);
        Assert.True(result.IsValid, result.Error?.Message);
        return result.Rules.ToArray();
    }

    private static string[] Texts(RuleConjunction conjunction)
    {
        return conjunction.Atoms.Select(a => a.Text).ToArray();
    }

    private static RuleAtom SingleAtom(TagRule rule)
    {
        return Assert.Single(Assert.Single(rule.Expression!.Alternatives).Atoms);
    }
}
