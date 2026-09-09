using System;
using System.Reflection;
using ChouUn.InventoryOrganizer.Adapter;
using ChouUn.InventoryOrganizer.Core.Inventory;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ChouUn.InventoryOrganizer.Patches;

/// <summary>
/// 接管容器面板的排序按钮。确认框由游戏保留，确认后进入这里。
/// 目前只把快照写进日志，原生排序照常执行；后续步骤替换为整理。
/// </summary>
internal sealed class GridSortPanelSortPatch : ModulePatch
{
    private static readonly AccessTools.FieldRef<GridSortPanel, CompoundItem> ItemField
        = AccessTools.FieldRefAccess<GridSortPanel, CompoundItem>("_item");

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(GridSortPanel), nameof(GridSortPanel.Sort));
    }

    [PatchPrefix]
    private static void Prefix(GridSortPanel __instance)
    {
        try
        {
            ItemSnapshot snapshot = SnapshotReader.Read(ItemField(__instance));
            foreach (string line in SnapshotDumper.Dump(snapshot))
            {
                Plugin.Log.LogInfo(line);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(ex);
        }
    }
}
