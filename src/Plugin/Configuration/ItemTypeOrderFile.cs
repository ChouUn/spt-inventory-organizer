using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace ChouUn.StashMaster.Configuration;

/// <summary>每次整理读取类型列表；无效编辑保留上次有效设置，不改写用户文件。</summary>
internal sealed class ItemTypeOrderFile
{
    internal static IReadOnlyList<string> DefaultOrder { get; } = new[]
    {
        "Containers", "Headsets", "Headgear", "NightAndThermalVision", "HeadgearArmor",
        "Eyewear", "Armor", "Rigs", "BallisticPlates", "Backpacks", "Weapons",
        "Magazines", "Ammo", "Grenades", "Meds", "Food", "Drink", "Facecovers",
        "Armband", "Melee", "Mods", "RepairKits", "SpecialEquipment", "Barter",
        "Keys", "Money", "Info",
    };

    private readonly string _path;
    private IReadOnlyList<string> _lastValid = Array.Empty<string>();

    internal ItemTypeOrderFile(string path) => _path = path;

    internal IReadOnlyList<string> Read(Action<string> warn, string? language = null)
    {
        try
        {
            if (!File.Exists(_path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using var stream = new FileStream(_path, FileMode.CreateNew);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                var generated = new ItemTypeOrderDocument
                {
                    ItemTypeOrder = DefaultOrder.Select(type =>
                        ItemTypeNames.Localize(type, language)).ToList(),
                };
                writer.Write(JsonConvert.SerializeObject(generated,
                    Formatting.Indented));
                return _lastValid = DefaultOrder.ToArray();
            }
            ItemTypeOrderDocument document =
                JsonConvert.DeserializeObject<ItemTypeOrderDocument>(
                    File.ReadAllText(_path))
                ?? throw new JsonException("配置不能为空");
            if (!document.Enabled) { return _lastValid = Array.Empty<string>(); }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (string name in document.ItemTypeOrder)
            {
                string type = ItemTypeNames.Resolve(name)
                    ?? throw new JsonException($"未知物品类型：{name}");
                if (!seen.Add(type))
                    throw new JsonException($"重复物品类型：{name}");
                order.Add(type);
            }
            return _lastValid = order
                .Concat(DefaultOrder.Where(type => !seen.Contains(type))).ToArray();
        }
        catch (Exception ex) when (ex is IOException
            || ex is UnauthorizedAccessException || ex is JsonException)
        {
            warn($"类型顺序配置无效，保留上次有效设置：{_path}：{ex.Message}");
            return _lastValid;
        }
    }
}

internal sealed class ItemTypeOrderDocument
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("itemTypeOrder", Required = Required.Always,
        ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> ItemTypeOrder { get; set; } =
        ItemTypeOrderFile.DefaultOrder.ToList();
}
