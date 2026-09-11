using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ChouUn.StashMaster.Compatibility;

/// <summary>只移除 UIFixes 的排序接管补丁，保留同一入口上的其他补丁。</summary>
internal static class UIFixesCompatibility
{
    public const string PluginId = "com.tyfon.uifixes";
    private const string PatchTypeName = "UIFixes.SortPatches+StackFirstPatch";

    /// <summary>
    /// 调用方须在 UIFixes 注册完成后调用。未安装时无需处理；
    /// 已安装但入口不符时拒绝继续接管，避免把版本不兼容误报成成功。
    /// </summary>
    public static bool DisableSortEntry(MethodBase target, Assembly? assembly)
    {
        if (assembly is null)
        {
            return false;
        }
        Type? patchType = assembly.GetType(PatchTypeName);
        MethodInfo? prefix = patchType?.GetMethod(
            "Prefix",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        HarmonyLib.Patches? patches = Harmony.GetPatchInfo(target);
        if (prefix is null || prefix.ReturnType != typeof(bool)
            || patches is null || !patches.Prefixes.Any(p => p.PatchMethod == prefix))
        {
            throw new InvalidOperationException(
                "UIFixes 兼容失败：未找到已注册的 StackFirstPatch.Prefix，未接管排序");
        }

        // 本机 Harmony 会执行所有前置补丁；return false 或优先级不能阻止另一条流程。
        var harmony = new Harmony("com.chouun.stashmaster.compatibility");
        harmony.Unpatch(target, prefix);
        if (Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.PatchMethod == prefix)
            == true)
        {
            throw new InvalidOperationException("UIFixes 兼容失败：排序入口补丁未移除");
        }
        return true;
    }
}
