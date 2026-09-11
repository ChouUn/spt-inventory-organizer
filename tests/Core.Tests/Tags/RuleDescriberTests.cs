using ChouUn.StashMaster.Core.Tags;
using Xunit;

namespace ChouUn.StashMaster.Core.Tests.Tags;

public sealed class RuleDescriberTests
{
    [Theory]
    [InlineData("@o", "兜底")]
    [InlineData("@o#3", "#3：兜底")]
    [InlineData("@o#1 n:5.45;", "#1：名称含 5.45")]
    [InlineData("@o ammo&&!n:5.45;", "类别 ammo 且 名称不含 5.45")]
    [InlineData("@o medications||n:grenade;", "类别 medications，或 名称含 grenade")]
    [InlineData("@o !ammo;", "类别非 ammo")]
    public void 复述规则(string text, string expected)
    {
        TagRule rule = Assert.Single(TagParser.Parse(text).Rules);

        Assert.Equal(expected, RuleDescriber.Describe(rule));
    }
}
