using System;
using System.Diagnostics;
using System.Reflection;
using ChouUn.InventoryOrganizer.Adapter;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Organizing;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ChouUn.InventoryOrganizer.Patches;

/// <summary>
/// 接管容器面板的排序按钮。确认框由游戏保留，确认后进入整理。
/// 整理结束后仍执行原生排序作为收尾，直到排布阶段落地。
/// </summary>
internal sealed class GridSortPanelSortPatch : ModulePatch
{
    private static readonly AccessTools.FieldRef<GridSortPanel, CompoundItem> ItemField
        = Field<CompoundItem>("_item");

    private static readonly AccessTools.FieldRef<GridSortPanel, InventoryController>
        ControllerField = Field<InventoryController>("_controller");

    private static AccessTools.FieldRef<GridSortPanel, T> Field<T>(string name)
    {
        return AccessTools.FieldRefAccess<GridSortPanel, T>(name);
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(GridSortPanel), nameof(GridSortPanel.Sort));
    }

    [PatchPrefix]
    private static bool Prefix(GridSortPanel __instance)
    {
        OrganizeAsync(__instance);
        return false;
    }

    private static async void OrganizeAsync(GridSortPanel panel)
    {
        try
        {
            CompoundItem root = ItemField(panel);
            InventoryController controller = ControllerField(panel);
            panel.ChangeProgress(inProgress: true);
            var stopwatch = Stopwatch.StartNew();
            ItemSnapshot snapshot = SnapshotReader.Read(root);
            long snapshotMs = stopwatch.ElapsedMilliseconds;
            var organizer = new Organizer(new GameInventoryPort(root, controller));
            OrganizeReport report = await organizer.RunAsync(snapshot);
            long organizeMs = stopwatch.ElapsedMilliseconds - snapshotMs;
            panel.ChangeProgress(inProgress: false);
            Notify(report);
            await panel.SortAsync();
            long sortMs = stopwatch.ElapsedMilliseconds - snapshotMs - organizeMs;
            Plugin.Log.LogInfo(
                $"timing: snapshot {snapshotMs} ms, organize {organizeMs} ms, " +
                $"native sort {sortMs} ms");
        }
        catch (Exception ex)
        {
            panel.ChangeProgress(inProgress: false);
            Plugin.Log.LogError(ex);
            NotificationManager.DisplayWarningNotification($"整理失败：{ex.Message}");
        }
    }

    private static void Notify(OrganizeReport report)
    {
        Plugin.Log.LogInfo(
            $"organize: folded {report.Folded}, failures {report.Failures.Count}");
        foreach (string failure in report.Failures)
        {
            Plugin.Log.LogWarning(failure);
        }
        string text = $"已折叠 {report.Folded} 件";
        if (report.Failures.Count > 0)
        {
            text += $"，{report.Failures.Count} 件失败，详见日志";
        }
        NotificationManager.DisplayMessageNotification(text);
    }
}
