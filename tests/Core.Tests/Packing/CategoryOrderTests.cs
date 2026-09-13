using System;
using System.IO;
using System.Linq;
using ChouUn.StashMaster.Core.Packing;
using Xunit;
using Xunit.Abstractions;

namespace ChouUn.StashMaster.Core.Tests.Packing;

public sealed class CategoryOrderTests
{
    private readonly ITestOutputHelper _output;
    public CategoryOrderTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void 调换类型顺序且不增加行数或类别跨度()
    {
        PackRequest request = Fixture() with
        {
            CategoryOrder = new[] { "Rigs", "Armor", "Ammo" },
        };
        var packer = new CachedPacker(new CpSatPacker());
        PackResult result = packer.PackAll(new[] { request }, 1).Grids[0];

        CpSatPackerTests.AssertValid(request, result);
        Assert.Equal(new[] { "rig", "armor", "ammo" }, result.Placements
            .OrderBy(p => p.Y).Select(p => p.Id));
        Assert.Equal(3, CpSatPacker.Height(request, result));
        Assert.Equal(new[] { 0 }, CategoryPacking.Spans(request, result));
    }

    [Fact]
    public void 调换顺序会使最终缓存失效()
    {
        PackRequest request = Fixture() with
        {
            CategoryOrder = new[] { "Rigs", "Armor", "Ammo" },
        };
        var packer = new CachedPacker(new CpSatPacker());
        PackResult before = packer.PackAll(new[] { request }, 1).Grids[0];
        request = request with
        {
            Current = before.Placements,
            CategoryOrder = new[] { "Ammo", "Armor", "Rigs" },
        };
        ContainerPackResult changed = packer.PackAll(new[] { request }, 1);

        Assert.Equal(new[] { "ammo", "armor", "rig" }, changed.Grids[0].Placements
            .OrderBy(p => p.Y).Select(p => p.Id));
        ContainerPackResult repeated = packer.PackAll(new[] { request with
        {
            Current = changed.Grids[0].Placements,
        } }, 1);
        Assert.Equal(changed.Grids, repeated.Grids);
    }

    [Fact]
    public void 类型按配置比较且缺席类型不参与()
    {
        PackRequest request = Fixture() with
        {
            CategoryOrder = new[] { "Weapons", "Rigs", "Ammo", "Armor" },
        };
        Assert.DoesNotContain(CategoryOrder.Pairs(request), p =>
            p.Before == "Weapons" || p.After == "Weapons");
        Assert.Contains(CategoryOrder.Pairs(request), p =>
            p.Before == "Rigs" && p.After == "Ammo");
        PackResult result = new CachedPacker(new CpSatPacker())
            .PackAll(new[] { request }, 1).Grids[0];
        Assert.Equal(new[] { "rig", "ammo", "armor" }, result.Placements
            .OrderBy(p => p.Y).Select(p => p.Id));
    }

    [Fact]
    public void 无手册数据也按类型排序并把未知类型放后面()
    {
        PackRequest request = Fixture() with
        {
            Items = Fixture().Items.Select(i => i with
            {
                SubcategoryPath = Array.Empty<string>(),
                SortType = i.Id == "rig" ? "" : i.SortType,
            }).ToArray(),
            CategoryOrder = new[] { "Armor", "Ammo" },
        };
        PackResult result = new CpSatPacker().Pack(request, 1);
        CpSatPackerTests.AssertValid(request, result);
        Assert.Equal(new[] { "armor", "ammo", "rig" }, result.Placements
            .OrderBy(p => p.Y).Select(p => p.Id));
    }

    [Fact]
    public void 可选收纳候选不受类型顺序与压紧提示约束()
    {
        var request = new PackRequest(3, 4, Array.Empty<FixedBlock>(), new[]
        {
            new PackItem("a", "a", 2, 2) { SortType = "Armor" },
            new PackItem("b", "b", 2, 2) { SortType = "Ammo" },
        })
        {
            CategoryOrder = new[] { "Ammo", "Armor" },
        };
        ContainerPackResult result = new GlobalPacker(new CpSatPacker())
            .Pack(new[] { request }, 1);
        Assert.True(result.Grids[0].Complete);
        CpSatPackerTests.AssertValid(request, result.Grids[0]);
        Assert.Equal(4, CpSatPacker.Height(request, result.Grids[0]));
    }

    [Fact]
    public void 类型变化使缓存失效且身份映射不能混淆类型()
    {
        PackRequest request = Fixture() with
        {
            Items = Fixture().Items.Select(i => i with
            {
                TemplateId = "same", SubcategoryPath = Array.Empty<string>(),
            }).ToArray(),
            CategoryOrder = new[] { "Rigs", "Armor", "Ammo" },
        };
        var packer = new CachedPacker(new CpSatPacker());
        PackResult before = packer.PackAll(new[] { request }, 1).Grids[0];
        request = request with
        {
            Current = before.Placements,
            Items = request.Items.Select(i => i with
            {
                SortType = i.SortType == "Rigs" ? "Ammo"
                    : i.SortType == "Ammo" ? "Rigs" : i.SortType,
            }).ToArray(),
        };
        ContainerPackResult changed = packer.PackAll(new[] { request }, 1);
        Assert.Equal(new[] { "ammo", "armor", "rig" }, changed.Grids[0].Placements
            .OrderBy(p => p.Y).Select(p => p.Id));
    }

    [Fact]
    public void 固定障碍下满足新顺序且不增加高度()
    {
        PackRequest request = Fixture() with
        {
            CategoryOrder = new[] { "Rigs", "Armor", "Ammo" },
            Fixed = new[] { new FixedBlock(0, 1, 1, 1) },
            Height = 4,
            Current = new[]
            {
                new Placement("ammo", 0, 0, false),
                new Placement("armor", 0, 2, false),
                new Placement("rig", 0, 3, false),
            },
        };
        var before = new PackResult(request.Current, Array.Empty<PackItem>());
        ContainerPackResult result = CategoryPackingModel.Solve(new[] { request },
            new ContainerPackResult(new[] { before }), 1, out _)!;

        Assert.NotNull(result);
        CpSatPackerTests.AssertValid(request, result.Grids[0]);
        Assert.True(CategoryOrder.Compare(request, result.Grids[0], before) < 0);
        Assert.Equal(CpSatPacker.Height(request, before),
            CpSatPacker.Height(request, result.Grids[0]));
        Assert.Equal(0, CategoryOrder.Penalty(request, result.Grids[0]));
        Assert.Equal(0, result.Grids[0].Placements.Single(p => p.Id == "rig").Y);
    }

    [Fact]
    public void 真实仓库反转类型顺序仍保持压紧()
    {
        PackRequest original = GeometryFixture();
        var baseline = new PackResult(original.Current, Array.Empty<PackItem>());
        var items = original.Items.ToDictionary(i => i.Id);
        string[] order = baseline.Placements
            .GroupBy(p => items[p.Id].SortType)
            .OrderByDescending(g => g.Min(p => p.Y) + g.Max(p => p.Y
                + (p.Rotated ? items[p.Id].Width : items[p.Id].Height)))
            .Select(g => g.Key).ToArray();
        PackRequest request = original with
            { Current = baseline.Placements, CategoryOrder = order };
        PackResult result = new GlobalPacker(new CpSatPacker())
            .Pack(new[] { request }, 2.5).Grids[0];

        CpSatPackerTests.AssertValid(request, result);
        _output.WriteLine($"before={CategoryOrder.Penalty(request, baseline)}; "
            + $"after={CategoryOrder.Penalty(request, result)}");
        _output.WriteLine(result.Diagnostic);
        Assert.True(CpSatPacker.Height(request, result)
            <= CpSatPacker.Height(request, baseline));
        Assert.Equal(0, CategoryOrder.Penalty(request, result));
        Assert.True(CategoryOrder.Compare(request, result, baseline) < 0);
    }

    [Fact]
    public void 真实仓库可以单独把中间类型前移()
    {
        PackRequest original = GeometryFixture();
        // 固定存档布局，不用限时求解的输出动态生成下一次测试输入。
        var baseline = new PackResult(original.Current, Array.Empty<PackItem>());
        var items = original.Items.ToDictionary(i => i.Id);
        string[] parents = baseline.Placements
            .GroupBy(p => items[p.Id].SortType)
            .OrderBy(g => g.Min(p => p.Y)).Select(g => g.Key).ToArray();
        string target = parents[parents.Length / 2];
        PackRequest request = original with
        {
            Current = baseline.Placements,
            CategoryOrder = new[] { target }.Concat(parents.Where(p => p != target))
                .ToArray(),
        };
        ContainerPackResult result = new GlobalPacker(new CpSatPacker())
            .Pack(new[] { request }, 2.5);
        CpSatPackerTests.AssertValid(request, result.Grids[0]);
        double Center(PackResult layout)
        {
            Placement[] group = layout.Placements
                .Where(p => items[p.Id].SortType == target)
                .ToArray();
            return group.Min(p => p.Y) + group.Max(p => p.Y
                + (p.Rotated ? items[p.Id].Width : items[p.Id].Height));
        }
        _output.WriteLine($"target={target}, {Center(baseline)}"
            + $"->{Center(result.Grids[0])}; {result.Diagnostic}");
        _output.WriteLine($"rows={CpSatPacker.Height(request, baseline)}"
            + $"->{CpSatPacker.Height(request, result.Grids[0])}; penalty="
            + CategoryOrder.Penalty(request, result.Grids[0]));
        Assert.True(CpSatPacker.Height(request, result.Grids[0])
            <= CpSatPacker.Height(request, baseline));
        Assert.Equal(0, CategoryOrder.Penalty(request, result.Grids[0]));
        Assert.True(Center(result.Grids[0]) < Center(baseline));
    }

    [Fact]
    public void SMH默认类型顺序和护甲优先均完整压紧真实仓库()
    {
        PackRequest original = GeometryFixture();
        string[] defaults =
        {
            "Ammo", "Grenades", "Magazines", "Weapons", "Headgear", "HeadgearArmor",
            "Facecovers", "Rigs", "NightAndThermalVision", "Eyewear", "Melee", "Meds",
            "Food", "Drink", "Mods", "RepairKits", "SpecialEquipment", "Barter", "Keys",
            "Money", "Armor", "Info", "Backpacks", "Headsets", "Containers",
            "BallisticPlates", "Armband",
        };
        foreach (string[] first in new[]
            { Array.Empty<string>(), new[] { "Armor", "Rigs", "Weapons" } })
        {
            PackRequest request = original with
                { CategoryOrder = first.Concat(defaults.Except(first)).ToArray() };
            ContainerPackResult result = new GlobalPacker(new CpSatPacker())
                .Pack(new[] { request }, 2.5);
            _output.WriteLine(result.Diagnostic);
            Assert.Null(result.Warning);
            CpSatPackerTests.AssertValid(request, result.Grids[0]);
            Assert.Equal(61, CpSatPacker.Height(request, result.Grids[0]));
            Assert.Equal(0, CategoryOrder.Penalty(request, result.Grids[0]));
        }
    }

    [Fact]
    public void 局部压紧保留跨模板物品和类型顺序并避开固定障碍()
    {
        PackItem[] items = Enumerable.Range(0, 4).Select(i =>
            new PackItem(i.ToString(), "template-" + i, 1, 1)
            {
                Required = true,
                SortType = i % 2 == 0 ? "Armor" : "Ammo",
                SubcategoryPath = new[] { "category-" + i },
            }).ToArray();
        var request = new PackRequest(2, 4,
            new[] { new FixedBlock(1, 1, 1, 1) }, items)
        {
            CategoryOrder = new[] { "Armor", "Ammo" },
            Current = items.Select((item, y) =>
                new Placement(item.Id, 0, y, false)).ToArray(),
        };
        var before = new PackResult(request.Current, Array.Empty<PackItem>());

        PackResult result = CategoryOrderCompaction.TryCompact(request, before, 1);

        Assert.True(result.Complete);
        CpSatPackerTests.AssertValid(request, result);
        Assert.True(CpSatPacker.Height(request, result)
            < CpSatPacker.Height(request, before));
        Assert.Equal(0, CategoryOrder.Penalty(request, result));
    }

    [Fact]
    public void 顺序受固定障碍阻挡时保留原位并提示且不缓存()
    {
        var request = new PackRequest(1, 4,
            new[] { new FixedBlock(0, 2, 1, 1) }, new[]
            {
                new PackItem("tall", "tall", 1, 2)
                    { Required = true, SortType = "tall" },
                new PackItem("small", "small", 1, 1)
                    { Required = true, SortType = "small" },
            })
        {
            Current = new[]
            {
                new Placement("tall", 0, 0, false),
                new Placement("small", 0, 3, false),
            },
            CategoryOrder = new[] { "small", "tall" },
        };
        var packer = new CachedPacker(new CpSatPacker());
        ContainerPackResult result = packer.PackAll(new[] { request }, 1);

        Assert.NotNull(result.Warning);
        Assert.Equal(request.Current, result.Grids[0].Placements);
        CpSatPackerTests.AssertValid(request, result.Grids[0]);
        ContainerPackResult repeated = packer.PackAll(new[] { request }, 0);
        Assert.NotNull(repeated.Warning);
        Assert.Equal(request.Current, repeated.Grids[0].Placements);
    }

    private static PackRequest Fixture() => new(1, 3, Array.Empty<FixedBlock>(), new[]
    {
        new PackItem("ammo", "a", 1, 1)
        {
            Required = true, SortType = "Ammo",
        },
        new PackItem("armor", "b", 1, 1)
        {
            Required = true, SortType = "Armor",
        },
        new PackItem("rig", "c", 1, 1)
        {
            Required = true, SortType = "Rigs",
        },
    });

    // 类型表由同一快照的运行时模板及游戏类型判定生成，模板编号与几何快照对应。
    // 此 fixture 只验证几何和类型顺序；真实类型不能继承匿名化的合成子链。
    private static PackRequest GeometryFixture()
    {
        using Stream stream = typeof(CategoryOrderTests).Assembly
            .GetManifestResourceStream(typeof(CategoryOrderTests).Namespace
                + ".Fixtures.stash-sort-types.csv")!;
        using var reader = new StreamReader(stream);
        var types = reader.ReadToEnd().Split(new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(','))
            .ToDictionary(row => row[0], row => row[1]);
        PackRequest request = ActualStashTests.Read("hierarchy-stash.csv");
        return request with
        {
            Items = request.Items.Select(i => i with
            {
                SortType = types[i.TemplateId],
                SubcategoryPath = Array.Empty<string>(),
            }).ToArray(),
        };
    }
}
