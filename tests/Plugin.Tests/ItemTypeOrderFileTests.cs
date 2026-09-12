using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChouUn.StashMaster.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ChouUn.StashMaster.Plugin.Tests;

public sealed class ItemTypeOrderFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "stash-master-order-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "category-order.json");
    private readonly List<string> _warnings = new();

    [Fact]
    public void 首次生成即启用默认类型顺序且后续读取一致()
    {
        string[] expected =
        {
            "Containers", "Headsets", "Headgear", "NightAndThermalVision",
            "HeadgearArmor", "Eyewear", "Armor", "Rigs", "BallisticPlates",
            "Backpacks", "Weapons", "Magazines", "Ammo", "Grenades", "Meds",
            "Food", "Drink", "Facecovers", "Armband", "Melee", "Mods", "RepairKits",
            "SpecialEquipment", "Barter", "Keys", "Money", "Info",
        };
        var file = new ItemTypeOrderFile(FilePath);
        Assert.Equal(expected, file.Read(_warnings.Add));
        JObject document = JObject.Parse(File.ReadAllText(FilePath));
        Assert.True(document.Value<bool>("enabled"));
        Assert.Equal(expected, document["itemTypeOrder"]!.Values<string>());
        Assert.Equal(27, document["itemTypeOrder"]!.Count());
        Assert.Null(document["categories"]);
        Assert.Equal(expected, file.Read(_warnings.Add));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void 未写开关默认启用且已有关闭设置保留()
    {
        Write(new ItemTypeOrderDocument());
        JObject document = JObject.Parse(File.ReadAllText(FilePath));
        document.Remove("enabled");
        File.WriteAllText(FilePath, document.ToString());
        Assert.Equal(ItemTypeOrderFile.DefaultOrder,
            new ItemTypeOrderFile(FilePath).Read(_warnings.Add));

        Write(new ItemTypeOrderDocument { Enabled = false });
        string before = File.ReadAllText(FilePath);
        Assert.Empty(new ItemTypeOrderFile(FilePath).Read(_warnings.Add));
        Assert.Equal(before, File.ReadAllText(FilePath));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void 热读取平铺顺序且缺省类型补在末尾()
    {
        var file = new ItemTypeOrderFile(FilePath);
        Write(new ItemTypeOrderDocument
        {
            Enabled = true,
            ItemTypeOrder = new List<string> { "Armor", "Rigs", "Weapons" },
        });
        IReadOnlyList<string> result = file.Read(_warnings.Add);
        Assert.Equal(new[] { "Armor", "Rigs", "Weapons", "Containers" },
            result.Take(4));
        Assert.Equal(27, result.Count);
        Assert.Equal(27, result.Distinct().Count());
        Write(new ItemTypeOrderDocument
        {
            Enabled = true,
            ItemTypeOrder = new List<string> { "Weapons", "Armor" },
        });
        Assert.Equal("Weapons", file.Read(_warnings.Add)[0]);
        Write(new ItemTypeOrderDocument { Enabled = false });
        Assert.Empty(file.Read(_warnings.Add));
        Assert.Empty(_warnings);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("null-list")]
    [InlineData("null-type")]
    [InlineData("missing-list")]
    public void 错误编辑保留上次顺序且不覆盖文件(string problem)
    {
        var file = new ItemTypeOrderFile(FilePath);
        Write(new ItemTypeOrderDocument { Enabled = true });
        IReadOnlyList<string> before = file.Read(_warnings.Add);
        var bad = new ItemTypeOrderDocument { Enabled = true };
        if (problem == "duplicate") { bad.ItemTypeOrder.Add("Armor"); }
        if (problem == "unknown") { bad.ItemTypeOrder.Add("Armour"); }
        Write(bad);
        if (problem == "invalid-json") { File.WriteAllText(FilePath, "{"); }
        if (problem == "null-list" || problem == "null-type"
            || problem == "missing-list")
        {
            JObject document = JObject.Parse(File.ReadAllText(FilePath));
            if (problem == "null-list")
                document["itemTypeOrder"] = JValue.CreateNull();
            if (problem == "null-type")
                document["itemTypeOrder"]![0] = JValue.CreateNull();
            if (problem == "missing-list") { document.Remove("itemTypeOrder"); }
            File.WriteAllText(FilePath, document.ToString());
        }
        string original = File.ReadAllText(FilePath);

        Assert.Equal(before, file.Read(_warnings.Add));
        Assert.Equal(original, File.ReadAllText(FilePath));
        Assert.Single(_warnings);
    }

    [Fact]
    public void 首次错误配置使用既有排序()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{");
        Assert.Empty(new ItemTypeOrderFile(FilePath).Read(_warnings.Add));
        Assert.Single(_warnings);
        Assert.Equal("{", File.ReadAllText(FilePath));
    }

    private void Write(ItemTypeOrderDocument document)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, JsonConvert.SerializeObject(document));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); }
    }
}
