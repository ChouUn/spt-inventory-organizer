using System;
using ChouUn.StashMaster.Adapter;
using Xunit;

namespace ChouUn.StashMaster.Plugin.Tests;

public sealed class ItemCategoryHierarchyTests
{
    private const string Money = "5b5f78b786f77447ed5636af";
    private const string Provisions = "5b47574386f77428ca22b340";
    private const string Drink = "5b47574386f77428ca22b335";
    private const string Barter = "5b47574386f77428ca22b33e";
    private const string Valuables = "5b47574386f77428ca22b2f1";

    [Fact]
    public void 四种货币直接归Money_GP不携带交换物品或贵重物品分支()
    {
        // 三种法币共享同一条原生链；GP 币从另一个原生分支迁入。
        Assert.Empty(ItemCategoryHierarchy.Subcategories("Money", new[] { Money }));
        Assert.Empty(ItemCategoryHierarchy.Subcategories("Money", new[] { Barter, Valuables }));
    }

    [Fact]
    public void 普通与模组饮料直接归Drink_真正饮品子类才保留()
    {
        // 普通饮料与 runtime 注册的 Monster 都挂在“饮食 / 饮品”下。
        Assert.Empty(ItemCategoryHierarchy.Subcategories("Drink", new[] { Provisions, Drink }));

        // 合成模组类别用于验证扩展层级，不冒充真实 Monster 的分类。
        Assert.Equal(new[] { "mod-drinks", "energy-drinks" },
            ItemCategoryHierarchy.Subcategories("Drink",
                new[] { Provisions, Drink, "mod-drinks", "energy-drinks" }));
    }

    [Theory]
    [InlineData("Meds", "5b47574386f77428ca22b344")]
    [InlineData("Weapons", "5b5f78dc86f77409407a7f8e")]
    [InlineData("Mods", "5b5f71a686f77447ed5636ab")]
    [InlineData("Keys", "5b47574386f77428ca22b342")]
    public void 类型内部细分从第一段保留_外部祖先数量不影响相对路径(
        string sortType, string boundary)
    {
        // 模组可以增加外部包装或内部细分；只有边界后的完整前缀影响子类分组。
        Assert.Equal(new[] { "internal-branch", "shared-leaf" },
            ItemCategoryHierarchy.Subcategories(sortType,
                new[] { boundary, "internal-branch", "shared-leaf" }));
        Assert.Equal(new[] { "internal-branch", "shared-leaf" },
            ItemCategoryHierarchy.Subcategories(sortType,
                new[] { "external-root", "external-wrapper", boundary,
                    "internal-branch", "shared-leaf" }));
        Assert.Equal(new[] { "other-branch", "shared-leaf" },
            ItemCategoryHierarchy.Subcategories(sortType,
                new[] { boundary, "other-branch", "shared-leaf" }));
    }

    [Fact]
    public void 跨原生分支迁入只归业务类型_不继承贵重或装备祖先()
    {
        foreach (string sortType in new[] { "Meds", "Weapons", "Mods", "Keys", "Armor", "Rigs" })
        {
            Assert.Empty(ItemCategoryHierarchy.Subcategories(sortType,
                new[] { Barter, Valuables }));
            Assert.Empty(ItemCategoryHierarchy.Subcategories(sortType,
                new[] { "old-equipment", "shared" }));
        }
    }

    [Fact]
    public void 空链未知类型与没有内部树的类型均不误继承原生分支()
    {
        Assert.Empty(ItemCategoryHierarchy.Subcategories("Drink", Array.Empty<string>()));
        foreach (string sortType in new[] { "", "ModOnlyType", "HeadgearArmor", "BallisticPlates" })
            Assert.Empty(ItemCategoryHierarchy.Subcategories(sortType,
                new[] { Provisions, Drink, "mod-drinks" }));
    }
}
