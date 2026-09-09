using System;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ChouUn.InventoryOrganizer.Patches;

/// <summary>保存 tag 时解析规则，用通知复述结果或指出错误。</summary>
internal sealed class EditTagWindowSavePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(EditTagWindow), nameof(EditTagWindow.Save));
    }

    [PatchPrefix]
    private static void Prefix(EditTagWindow __instance)
    {
        try
        {
            TagFeedback.Report(__instance._tagInput.text);
        }
        catch (Exception ex)
        {
            // 提示失败不能影响游戏自己的保存流程。
            Plugin.Log.LogError(ex);
        }
    }
}
