using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using ChouUn.InventoryOrganizer.Compatibility;
using ChouUn.InventoryOrganizer.Patches;
using EFT.UI.DragAndDrop;
using HarmonyLib;

namespace ChouUn.InventoryOrganizer;

[BepInPlugin("com.chouun.inventoryorganizer", "Inventory Organizer", "0.1.0")]
[BepInDependency(
    UIFixesCompatibility.PluginId, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;

        // 必须先于任何触碰 OR-Tools 类型的代码：
        // 失败的 P/Invoke 初始化在进程内不可恢复。
        string pluginDir = Path.GetDirectoryName(Info.Location)!;
        NativeLibraries.Load(pluginDir);
        Logger.LogInfo("OR-Tools native entry and dependencies loaded");

        new EditTagWindowShowPatch().Enable();
        new EditTagWindowSavePatch().Enable();
        // 软依赖保证 UIFixes 的 Awake 先注册补丁，未安装时仍可独立加载。
        Chainloader.PluginInfos.TryGetValue(
            UIFixesCompatibility.PluginId, out var uiFixes);
        bool removed = UIFixesCompatibility.DisableSortEntry(
            AccessTools.Method(typeof(GridSortPanel), nameof(GridSortPanel.Sort)),
            uiFixes?.Instance.GetType().Assembly);
        Logger.LogInfo(removed
            ? $"UIFixes {uiFixes!.Metadata.Version}: StackFirstPatch removed; " +
              "other patches and settings preserved; organizer owns stacking"
            : "UIFixes not installed; organizer owns sorting");
        new GridSortPanelSortPatch().Enable();
        new LayoutDiagnosticsPatch("ExecuteInternal").Enable();
        new LayoutDiagnosticsPatch("Dispose").Enable();
        new NativeSortDiagnosticsPatch().Enable();
        Logger.LogInfo($"loaded from {pluginDir}");
    }
}
