using EFT.InventoryLogic;

namespace ChouUn.StashMaster.Adapter;

/// <summary>沿用 SMH 的物品类型；派生类型先于宽泛基类匹配。</summary>
internal static class ItemTypeClassifier
{
    internal static string Classify(Item item) => item switch
    {
        SpecialScope or SpecialWeapon => "NightAndThermalVision",
        Armor => "Armor",
        ArmorPlate => "BallisticPlates",
        Visors => "Eyewear",
        Headwear => "Headgear",
        FaceCover => "Facecovers",
        ArmoredEquipment => "HeadgearArmor",
        Knife => "Melee",
        Weapon => "Weapons",
        Magazine => "Magazines",
        Ammo or AmmoBox => "Ammo",
        Meds => "Meds",
        Food => "Food",
        Drink => "Drink",
        Mod => "Mods",
        ThrowWeap => "Grenades",
        BarterItem or Flyer => "Barter",
        Vest => "Rigs",
        Headphones => "Headsets",
        Key => "Keys",
        SimpleContainer => "Containers",
        Backpack => "Backpacks",
        RepairKit => "RepairKits",
        SpecItem => "SpecialEquipment",
        Money => "Money",
        Info => "Info",
        ArmBand => "Armband",
        _ => "",
    };
}
