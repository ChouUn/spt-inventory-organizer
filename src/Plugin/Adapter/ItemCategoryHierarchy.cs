using System;
using System.Collections.Generic;

namespace ChouUn.StashMaster.Adapter;

/// <summary>将完整手册链截在业务类型边界，只保留该类型内部的原生细分。</summary>
internal static class ItemCategoryHierarchy
{
    internal static IReadOnlyList<string> Subcategories(string sortType,
        IReadOnlyList<string> nativePath)
    {
        // 锚点表示类型对应的原生分类，不是快照中多数成员所在的类别。
        // 跨分支归入此类型的物品直接挂在类型根上，不能携带其旧祖先。
        string? boundary = sortType switch
        {
            "Containers" => "5b5f6fa186f77409407a7eb7",
            "Headsets" => "5b5f6f3c86f774094242ef87",
            "Headgear" => "5b47574386f77428ca22b330",
            "NightAndThermalVision" => "5b5f749986f774094242f199",
            "Eyewear" => "5b47574386f77428ca22b331",
            "Armor" => "5b5f701386f774093f2ecf0f",
            "Rigs" => "5b5f6f8786f77447ed563642",
            "Backpacks" => "5b5f6f6c86f774093f2ecf0b",
            "Weapons" => "5b5f78dc86f77409407a7f8e",
            "Magazines" => "5b5f754a86f774094242f19b",
            "Ammo" => "5b47574386f77428ca22b346",
            "Grenades" => "5b5f7a2386f774093f2ed3c4",
            "Meds" => "5b47574386f77428ca22b344",
            "Food" => "5b47574386f77428ca22b336",
            "Drink" => "5b47574386f77428ca22b335",
            "Facecovers" => "5b47574386f77428ca22b32f",
            "Melee" => "5b5f7a0886f77409407a7f96",
            "Mods" => "5b5f71a686f77447ed5636ab",
            "SpecialEquipment" => "5b47574386f77428ca22b345",
            "Barter" => "5b47574386f77428ca22b33e",
            "Keys" => "5b47574386f77428ca22b342",
            "Money" => "5b5f78b786f77447ed5636af",
            "Info" => "5b47574386f77428ca22b341",
            // 没有对应原生内部树，不能借用更宽泛的“装备组件”或“特殊装备”。
            "HeadgearArmor" or "BallisticPlates" or "Armband" or "RepairKits" => null,
            _ => null,
        };
        if (boundary == null) { return Array.Empty<string>(); }
        for (int index = 0; index < nativePath.Count; index++)
        {
            if (nativePath[index] != boundary) { continue; }
            int count = nativePath.Count - index - 1;
            if (count == 0) { return Array.Empty<string>(); }
            var result = new string[count];
            for (int child = 0; child < count; child++)
                result[child] = nativePath[index + child + 1];
            return result;
        }
        return Array.Empty<string>();
    }
}
