using System.Collections.Generic;
using System.IO;

namespace ChouUn.StashMaster.Configuration;

/// <summary>向一次整理提供稳定的物品类型顺序快照。</summary>
internal static class ItemTypeOrder
{
    private static readonly ItemTypeOrderFile File = new(Path.Combine(
        BepInEx.Paths.ConfigPath, "com.chouun.stashmaster.category-order.json"));

    internal static IReadOnlyList<string> Read() =>
        File.Read(message => Plugin.Log.LogWarning(message));
}
