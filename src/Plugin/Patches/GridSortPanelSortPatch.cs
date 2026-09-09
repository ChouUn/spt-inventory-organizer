using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using ChouUn.InventoryOrganizer.Adapter;
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
            var organizer = new Organizer(new GameInventoryPort(root, controller));
            OrganizeReport report = await organizer.RunAsync();
            long organizeMs = stopwatch.ElapsedMilliseconds;
            panel.ChangeProgress(inProgress: false);
            Notify(report);
            await panel.SortAsync();
            long sortMs = stopwatch.ElapsedMilliseconds - organizeMs;
            Plugin.Log.LogInfo(
                $"timing: organize {organizeMs} ms, native sort {sortMs} ms");
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
            $"organize: folded {report.Folded}, moved {report.Moved}, " +
            $"warnings {report.Warnings.Count}, failures {report.Failures.Count}");
        foreach (string line in report.Warnings.Concat(report.Failures))
        {
            Plugin.Log.LogWarning(line);
        }
        string text = $"已折叠 {report.Folded} 件，收纳 {report.Moved} 件";
        int problems = report.Warnings.Count + report.Failures.Count;
        if (problems > 0)
        {
            text += $"，{problems} 条警告，详见日志";
        }
        NotificationManager.DisplayMessageNotification(text);
    }
}
