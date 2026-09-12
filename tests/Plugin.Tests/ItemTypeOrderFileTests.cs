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
        string original = File.ReadAllText(FilePath);
        Assert.Equal(expected, file.Read(_warnings.Add, "ch"));
        Assert.Equal(original, File.ReadAllText(FilePath));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void 中文配置生成后切换语言及重新加载仍识别且不改写()
    {
        var file = new ItemTypeOrderFile(FilePath);
        Assert.Equal(ItemTypeOrderFile.DefaultOrder, file.Read(_warnings.Add, "ch"));
        string original = File.ReadAllText(FilePath);
        string[] names = JObject.Parse(original)["itemTypeOrder"]!
            .Values<string>().ToArray()!;
        Assert.Equal(new[] { "容器", "耳机", "头部装备" }, names.Take(3));
        Assert.Equal(27, names.Distinct().Count());
        Assert.All(names, name => Assert.DoesNotContain(name,
            ItemTypeOrderFile.DefaultOrder));
        Assert.Equal(ItemTypeOrderFile.DefaultOrder, file.Read(_warnings.Add, "ch"));
        Assert.Equal(ItemTypeOrderFile.DefaultOrder,
            new ItemTypeOrderFile(FilePath).Read(_warnings.Add, "en"));
        Assert.Equal(original, File.ReadAllText(FilePath));
        Assert.Empty(_warnings);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData(null)]
    public void 无对应译名时生成英文配置(string? language)
    {
        new ItemTypeOrderFile(FilePath).Read(_warnings.Add, language);
        JToken names = JObject.Parse(File.ReadAllText(FilePath))["itemTypeOrder"]!;
        Assert.Equal(ItemTypeOrderFile.DefaultOrder, names.Values<string>());
        Assert.Empty(_warnings);
    }

    [Fact]
    public void 中英文混写按同一类型补齐且热读取编辑()
    {
        var file = new ItemTypeOrderFile(FilePath);
        Write(new ItemTypeOrderDocument
        {
            ItemTypeOrder = new List<string> { "护甲", "Rigs", "枪械" },
        });
        string original = File.ReadAllText(FilePath);
        IReadOnlyList<string> result = file.Read(_warnings.Add, "ch");
        Assert.Equal(new[] { "Armor", "Rigs", "Weapons", "Containers" },
            result.Take(4));
        Assert.Equal(27, result.Distinct().Count());
        Assert.Equal(original, File.ReadAllText(FilePath));
        Write(new ItemTypeOrderDocument
        {
            ItemTypeOrder = new List<string> { "枪械", "Armor" },
        });
        Assert.Equal("Weapons", file.Read(_warnings.Add, "ch")[0]);
        Assert.Empty(_warnings);
    }

    [Theory]
    [InlineData("护甲", "Armor")]
    [InlineData("Armor", "护甲")]
    [InlineData("护甲", "护甲")]
    public void 中英文重复类型保留上次有效配置(string first, string second)
    {
        var file = new ItemTypeOrderFile(FilePath);
        IReadOnlyList<string> before = file.Read(_warnings.Add, "ch");
        Write(new ItemTypeOrderDocument
        {
            ItemTypeOrder = new List<string> { first, second },
        });
        string original = File.ReadAllText(FilePath);
        Assert.Equal(before, file.Read(_warnings.Add, "ch"));
        Assert.Single(_warnings);
        Assert.Equal(original, File.ReadAllText(FilePath));
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
