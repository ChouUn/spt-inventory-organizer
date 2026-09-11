using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ChouUn.StashMaster.Patches;

/// <summary>
/// 放宽 tag 输入框的长度上限。Show 之前设，避免已保存的长 tag 被截断；
/// Show 之后再设一次并刷新「已输入/上限」计数。
/// </summary>
internal sealed class EditTagWindowShowPatch : ModulePatch
{
    public const int CharacterLimit = 256;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(EditTagWindow),
            nameof(EditTagWindow.Show),
            new[] { typeof(TagComponent) });
    }

    [PatchPrefix]
    private static void Prefix(EditTagWindow __instance)
    {
        __instance._tagInput.characterLimit = CharacterLimit;
    }

    [PatchPostfix]
    private static void Postfix(EditTagWindow __instance)
    {
        __instance._tagInput.characterLimit = CharacterLimit;
        __instance.TagTextChange(__instance._tagInput.text);
    }
}
