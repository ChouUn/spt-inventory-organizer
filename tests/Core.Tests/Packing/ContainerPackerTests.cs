using System;
using System.Linq;
using ChouUn.StashMaster.Core.Packing;
using Xunit;

namespace ChouUn.StashMaster.Core.Tests.Packing;

public sealed class ContainerPackerTests
{
    [Fact]
    public void 混合尺寸过滤与固定障碍的联合结果合法且不劣于保底()
    {
        var random = new Random(42);
        var packer = new ContainerPacker(new CpSatPacker());
        for (int sample = 0; sample < 12; sample++)
        {
            PackItem[] candidates = Enumerable.Range(0, 8).Select(i =>
                new PackItem("candidate" + i, "t", random.Next(1, 4),
                    random.Next(1, 4))).ToArray();
            PackRequest[] requests = Enumerable.Range(0, 3).Select(grid =>
            {
                var required = new PackItem("required" + grid, "t", 1, 1)
                {
                    Required = true,
                };
                return Grid(3, 4, new[] { required }.Concat(candidates
                    .Where(_ => random.Next(3) != 0)).ToArray()) with
                {
                    Fixed = new[] { new FixedBlock(0, 0, 1, 2) },
                    Current = new[] { new Placement(required.Id, 0, 3, false) },
                };
            }).ToArray();
            ContainerPackResult baseline = packer.Pack(requests, 0);
            ContainerPackResult result = packer.Pack(requests, 0.1);

            Valid(requests, result);
            int before = requests.Select((r, i) =>
                CpSatPackerTests.Area(r, baseline.Grids[i])).Sum();
            int after = requests.Select((r, i) =>
                CpSatPackerTests.Area(r, result.Grids[i])).Sum();
            Assert.True(after >= before);
        }
    }

    [Fact]
    public void 联合选择把方块留给小网格_长条装满大网格()
    {
        var square = new PackItem("square", "t", 2, 2);
        PackItem[] bars = Enumerable.Range(0, 3)
            .Select(i => new PackItem("bar" + i, "t", 1, 3)).ToArray();
        PackItem[] items = new[] { square }.Concat(bars).ToArray();
        var requests = new[] { Grid(3, 3, items), Grid(2, 2, items) };
        var packer = new ContainerPacker(new CachedPacker(new CpSatPacker()));

        ContainerPackResult baseline = packer.Pack(requests, 0);
        ContainerPackResult result = packer.Pack(requests, 1);

        Assert.True(baseline.Grids.Sum(g => g.Placements.Count) < 4);
        Assert.Equal(3, result.Grids[0].Placements.Count);
        Assert.Equal("square", Assert.Single(result.Grids[1].Placements).Id);
        Valid(requests, result);
    }

    [Fact]
    public void 不同过滤的单格必须联合分配_不能重复或遗漏受限候选()
    {
        var flexible = new PackItem("a", "t", 1, 1);
        var restricted = new PackItem("b", "t", 1, 1);
        var requests = new[]
        {
            Grid(1, 1, flexible, restricted), Grid(1, 1, flexible),
        };

        ContainerPackResult result = new ContainerPacker(new CpSatPacker())
            .Pack(requests, 1);

        Assert.Equal("b", Assert.Single(result.Grids[0].Placements).Id);
        Assert.Equal("a", Assert.Single(result.Grids[1].Placements).Id);
        Valid(requests, result);
    }

    [Fact]
    public void 必留单格与大件固定在所属网格_障碍不移动()
    {
        var existing = new PackItem("existing", "t", 2, 2) { Required = true };
        var single = new PackItem("single", "t", 1, 1) { Required = true };
        var incoming = new PackItem("incoming", "t", 1, 2);
        var requests = new[]
        {
            Grid(3, 3, existing, incoming) with
            {
                Fixed = new[] { new FixedBlock(2, 2, 1, 1) },
                Current = new[] { new Placement("existing", 0, 0, false) },
            },
            Grid(1, 3, single, incoming) with
            {
                Current = new[] { new Placement("single", 0, 2, false) },
            },
        };

        ContainerPackResult result = new ContainerPacker(new CpSatPacker())
            .Pack(requests, 1);

        Assert.Contains(result.Grids[0].Placements, p => p.Id == "existing");
        Assert.Contains(result.Grids[1].Placements, p => p.Id == "single");
        Valid(requests, result);
    }

    private static PackRequest Grid(int width, int height, params PackItem[] items) =>
        new(width, height, Array.Empty<FixedBlock>(), items);

    private static void Valid(PackRequest[] requests, ContainerPackResult result)
    {
        string[] ids = result.Grids.SelectMany(g => g.Placements)
            .Select(p => p.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        for (int i = 0; i < requests.Length; i++)
        {
            CpSatPackerTests.AssertValid(requests[i], result.Grids[i]);
            Assert.DoesNotContain(result.Grids[i].Unplaced, p => p.Required);
        }
    }
}
