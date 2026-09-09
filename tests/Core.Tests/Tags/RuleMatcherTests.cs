using System;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Tags;
using Xunit;

namespace ChouUn.InventoryOrganizer.Core.Tests.Tags;

public sealed class RuleMatcherTests
{
    private static readonly string[] Categories =
    {
        "武器零件&配件", "装备配件", "弹匣",
    };

    private static readonly ItemSnapshot Magazine = new ItemSnapshot(
        "m", "t", "TROY Battlemag 5.56x45 STANAG 30发弹匣", "Battlemag",
        Categories, 1, 2, new GridPosition(0, 0, false),
        false, false, LockState.Free, null, Array.Empty<GridSnapshot>());

    [Theory]
    [InlineData("@o 弹匣;", true)]
    [InlineData("@o 装备配件;", true)]
    [InlineData("@o 武器零件&配件;", true)]
    [InlineData("@o 弹药;", false)]
    [InlineData("@o n:STANAG;", true)]
    [InlineData("@o n:stanag;", true)]
    [InlineData("@o n:battlemag;", true)]
    [InlineData("@o n:5.45;", false)]
    [InlineData("@o !弹匣;", false)]
    [InlineData("@o 弹匣&&!n:TROY;", false)]
    [InlineData("@o 弹匣&&n:TROY;", true)]
    [InlineData("@o 弹药||n:5.56;", true)]
    [InlineData("@o n:ak&&n:74||弹匣;", true)]
    [InlineData("@o", true)]
    public void 按类别相等与名称包含匹配(string tag, bool expected)
    {
        TagRule rule = Assert.Single(TagParser.Parse(tag).Rules);

        Assert.Equal(expected, RuleMatcher.Matches(rule, Magazine));
    }
}
