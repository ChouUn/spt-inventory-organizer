#if DEBUG
using System;
using System.Runtime.Serialization;
using ChouUn.StashMaster.Adapter;
using ChouUn.StashMaster.Configuration;
using EFT.InventoryLogic;
using Xunit;

namespace ChouUn.StashMaster.Plugin.Tests;

public sealed class ItemTypeClassifierTests
{
    [Theory]
    [InlineData(typeof(SpecialScope), "NightAndThermalVision")]
    [InlineData(typeof(SpecialWeapon), "NightAndThermalVision")]
    [InlineData(typeof(Armor), "Armor")]
    [InlineData(typeof(ArmorPlate), "BallisticPlates")]
    [InlineData(typeof(Visors), "Eyewear")]
    [InlineData(typeof(Headwear), "Headgear")]
    [InlineData(typeof(FaceCover), "Facecovers")]
    [InlineData(typeof(ArmoredEquipment), "HeadgearArmor")]
    [InlineData(typeof(Knife), "Melee")]
    [InlineData(typeof(AmmoBox), "Ammo")]
    [InlineData(typeof(Ammo), "Ammo")]
    [InlineData(typeof(Vest), "Rigs")]
    [InlineData(typeof(Backpack), "Backpacks")]
    [InlineData(typeof(SimpleContainer), "Containers")]
    public void 派生类型按SMH归类且配置可识别(Type runtimeType, string expected)
    {
        // 分类只检查运行时类型，无需初始化 Unity 中的物品状态。
        var item = (Item)FormatterServices.GetUninitializedObject(runtimeType);
        string result = ItemTypeClassifier.Classify(item);
        Assert.Equal(expected, result);
        Assert.Contains(result, ItemTypeOrderFile.DefaultOrder);
    }

    [Fact]
    public void 海报归杂物并保留手册中的其他子类()
    {
        var flyer = (Item)FormatterServices.GetUninitializedObject(typeof(Flyer));
        string sortType = ItemTypeClassifier.Classify(flyer);

        Assert.Equal("Barter", sortType);
        Assert.Equal(new[] { "5b47574386f77428ca22b2f4" },
            ItemCategoryHierarchy.Subcategories(sortType, new[]
            {
                "5b47574386f77428ca22b33e",
                "5b47574386f77428ca22b2f4",
            }));
    }

    [Fact]
    public void 未识别物品保留为空类型()
    {
        var item = (Item)FormatterServices.GetUninitializedObject(typeof(Item));
        Assert.Equal("", ItemTypeClassifier.Classify(item));
    }
}
#endif
