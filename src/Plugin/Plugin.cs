using System.IO;
using BepInEx;

namespace ChouUn.InventoryOrganizer;

[BepInPlugin("com.chouun.inventoryorganizer", "Inventory Organizer", "0.1.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        // 必须先于任何触碰 OR-Tools 类型的代码：
        // 失败的 P/Invoke 初始化在进程内不可恢复。
        string pluginDir = Path.GetDirectoryName(Info.Location)!;
        NativeLibraries.AddSearchDirectory(pluginDir);
        Logger.LogInfo($"loaded from {pluginDir}");
    }
}
