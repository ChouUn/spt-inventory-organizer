using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using ChouUn.StashMaster.Adapter;
using ChouUn.StashMaster.Core.Organizing;
using ChouUn.StashMaster.Core.Packing;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace ChouUn.StashMaster.Patches;

/// <summary>
/// 接管容器面板的排序按钮。确认框由游戏保留，确认后进入整理，不再执行原生排序。
/// </summary>
internal sealed class GridSortPanelSortPatch : ModulePatch
{
    internal static bool IsOrganizing { get; private set; }

    private static readonly IPacker Packer = new CachedPacker(new CpSatPacker());

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
        if (IsOrganizing)
        {
            return false;
        }
        OrganizeAsync(__instance);
        return false;
    }

    private static async void OrganizeAsync(GridSortPanel panel)
    {
        IsOrganizing = true;
        try
        {
            CompoundItem root = ItemField(panel);
            InventoryController controller = ControllerField(panel);
            panel.ChangeProgress(inProgress: true);
            var stopwatch = Stopwatch.StartNew();
            var organizer = new Organizer(
                new GameInventoryPort(root, controller), Packer);
            OrganizeReport report = await organizer.RunAsync();
            Notify(report);
            Plugin.Log.LogInfo(
                $"timing: organize {stopwatch.ElapsedMilliseconds} ms, " +
                $"pack planning {report.PackPlanningMilliseconds} ms, " +
                $"merge transactions {report.MergeMilliseconds} ms");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(ex);
            NotificationManager.DisplayWarningNotification($"整理失败：{ex.Message}");
        }
        finally
        {
            IsOrganizing = false;
            panel.ChangeProgress(inProgress: false);
        }
    }

    private static void Notify(OrganizeReport report)
    {
        foreach (string diagnostic in report.Diagnostics)
        {
            Plugin.Log.LogInfo(diagnostic);
        }
        Plugin.Log.LogInfo(
            $"organize: folded {report.Folded}, moved {report.Moved}, " +
            $"merged {report.Merged}, packed {report.Packed}, " +
            $"warnings {report.Warnings.Count}, " +
            $"failures {report.Failures.Count}");
        foreach (string line in report.Warnings.Concat(report.Failures))
        {
            Plugin.Log.LogWarning(line);
        }
        string text = $"已折叠 {report.Folded} 件，合并 {report.Merged} 次，" +
                      $"收纳 {report.Moved} 件，" +
                      $"排布 {report.Packed} 个网格";
        int problems = report.Warnings.Count + report.Failures.Count;
        if (problems > 0)
        {
            text += $"，{problems} 条警告，详见日志";
        }
        NotificationManager.DisplayMessageNotification(text);
    }
}
