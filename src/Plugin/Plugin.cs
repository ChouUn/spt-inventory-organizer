using System.IO;
using BepInEx;
using BepInEx.Logging;
using ChouUn.InventoryOrganizer.Patches;

namespace ChouUn.InventoryOrganizer;

[BepInPlugin("com.chouun.inventoryorganizer", "Inventory Organizer", "0.1.0")]
public sealed class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;

        // 必须先于任何触碰 OR-Tools 类型的代码：
        // 失败的 P/Invoke 初始化在进程内不可恢复。
        string pluginDir = Path.GetDirectoryName(Info.Location)!;
        NativeLibraries.AddSearchDirectory(pluginDir);

        new EditTagWindowShowPatch().Enable();
        new EditTagWindowSavePatch().Enable();
        new GridSortPanelSortPatch().Enable();
        Logger.LogInfo($"loaded from {pluginDir}");
    }
}
