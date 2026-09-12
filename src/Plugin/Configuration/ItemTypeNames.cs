using System;
using System.Collections.Generic;
using System.Linq;

namespace ChouUn.StashMaster.Configuration;

/// <summary>配置使用可读译名，排序器始终接收稳定的英文类型名。</summary>
internal static class ItemTypeNames
{
    private static readonly IReadOnlyDictionary<string, string> Chinese =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Containers"] = "容器",
            ["Headsets"] = "耳机",
            ["Headgear"] = "头部装备",
            ["NightAndThermalVision"] = "夜视与热成像",
            ["HeadgearArmor"] = "头盔装甲",
            ["Eyewear"] = "眼镜",
            ["Armor"] = "护甲",
            ["Rigs"] = "胸挂",
            ["BallisticPlates"] = "插板",
            ["Backpacks"] = "背包",
            ["Weapons"] = "枪械",
            ["Magazines"] = "弹匣",
            ["Ammo"] = "弹药",
            ["Grenades"] = "投掷物",
            ["Meds"] = "医疗",
            ["Food"] = "食物",
            ["Drink"] = "饮料",
            ["Facecovers"] = "面罩",
            ["Armband"] = "臂章",
            ["Melee"] = "近战",
            ["Mods"] = "配件",
            ["RepairKits"] = "维修包",
            ["SpecialEquipment"] = "特殊装备",
            ["Barter"] = "杂物",
            ["Keys"] = "钥匙",
            ["Money"] = "货币",
            ["Info"] = "情报",
        };

    internal static string Localize(string type, string? language) =>
        language == "ch" && Chinese.TryGetValue(type, out string name) ? name : type;

    /// <summary>同时接受已有英文名和译名，使配置不受后续游戏语言切换影响。</summary>
    internal static string? Resolve(string? name)
    {
        if (ItemTypeOrderFile.DefaultOrder.Contains(name)) { return name; }
        foreach (var pair in Chinese)
        {
            if (pair.Value == name) { return pair.Key; }
        }
        return null;
    }
}
