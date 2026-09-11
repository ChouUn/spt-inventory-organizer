using System.Diagnostics;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ChouUn.StashMaster.Patches;

/// <summary>分别测量布局落位和事件刷新，避免把整个事务等待误当成界面耗时。</summary>
internal sealed class LayoutDiagnosticsPatch : ModulePatch
{
    private readonly string _method;

    public LayoutDiagnosticsPatch(string method)
    {
        _method = method;
    }

    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(ApplyInventoryChangesOperation), _method);

    [PatchPrefix]
    private static void Prefix(out Stopwatch __state)
    {
        __state = Stopwatch.StartNew();
    }

    [PatchPostfix]
    private static void Postfix(MethodBase __originalMethod, Stopwatch __state)
    {
        double elapsedMs = __state.Elapsed.TotalMilliseconds;
        Plugin.Log.LogInfo(
            $"layout {__originalMethod.Name}: {elapsedMs:F1} ms");
    }
}

/// <summary>验收时识别整理期间是否仍有人调用原生排序，只观察，不干预执行。</summary>
internal sealed class NativeSortDiagnosticsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(ItemManipulator), nameof(ItemManipulator.Sort));

    [PatchPrefix]
    private static void Prefix()
    {
        if (GridSortPanelSortPatch.IsOrganizing)
        {
            Plugin.Log.LogWarning("native sort invoked during organize");
        }
    }
}
