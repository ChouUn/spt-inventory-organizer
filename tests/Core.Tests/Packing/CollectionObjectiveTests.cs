using System;
using System.Linq;
using ChouUn.StashMaster.Core.Packing;
using Xunit;

namespace ChouUn.StashMaster.Core.Tests.Packing;

public sealed class CollectionObjectiveTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void 全部候选能放下即结束_不压紧或聚合原有物品(int grids)
    {
        PackRequest[] requests = Enumerable.Range(0, grids).Select(g =>
            new PackRequest(2, 6, Array.Empty<FixedBlock>(), new[]
            {
                Item(g + "a", "weapon", true), Item(g + "b", "gear", true),
                Item(g + "c", "weapon", true), Item(g + "d", "gear", true),
                Item(g + "incoming", "weapon", false),
            })
            {
                Current = new[]
                {
                    new Placement(g + "a", 0, 2, false),
                    new Placement(g + "b", 1, 2, false),
                    new Placement(g + "c", 0, 5, false),
                    new Placement(g + "d", 1, 5, false),
                },
            }).ToArray();

        ContainerPackResult result = new ContainerPacker(new CpSatPacker())
            .Pack(requests, 1);

        for (int g = 0; g < grids; g++)
        {
            Assert.True(result.Grids[g].Complete);
            Assert.All(requests[g].Current, p =>
                Assert.Contains(p, result.Grids[g].Placements));
            CpSatPackerTests.AssertValid(requests[g], result.Grids[g]);
        }
    }

    [Fact]
    public void 固定障碍扣除后面积已满_未选候选不触发聚合求解()
    {
        var request = new PackRequest(2, 3, new[] { new FixedBlock(0, 0, 2, 1) },
            Enumerable.Range(0, 5).Select(i => Item(i.ToString(),
                i % 2 == 0 ? "weapon" : "gear", i < 4)).ToArray())
        {
            Current = Enumerable.Range(0, 4)
                .Select(i => new Placement(i.ToString(), i % 2, 1 + i / 2, false))
                .ToArray(),
        };

        ContainerPackResult result = new ContainerPacker(new CpSatPacker())
            .Pack(new[] { request }, 1);

        Assert.Equal("4", Assert.Single(result.Grids[0].Unplaced).Id);
        Assert.All(request.Current, p =>
            Assert.Contains(p, result.Grids[0].Placements));
        CpSatPackerTests.AssertValid(request, result.Grids[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 需要求解时只增加面积_类别不影响候选选择(bool categories)
    {
        var square = new PackItem("square", "square", 2, 2);
        PackItem[] bars = Enumerable.Range(0, 3)
            .Select(i => new PackItem("bar" + i, "bar", 1, 3)).ToArray();
        PackItem[] items = new[] { square }.Concat(bars).Select(i => i with
        {
            SortType = categories ? "parent" : "",
            SubcategoryPath = categories ? new[] { i.TemplateId }
                : Array.Empty<string>(),
        }).ToArray();
        var request = new PackRequest(3, 3, Array.Empty<FixedBlock>(), items);

        ContainerPackResult result = new ContainerPacker(new CpSatPacker())
            .Pack(new[] { request }, 1);

        Assert.Equal(new[] { "bar0", "bar1", "bar2" },
            result.Grids[0].Placements.Select(p => p.Id).OrderBy(id => id));
        CpSatPackerTests.AssertValid(request, result.Grids[0]);
    }

    private static PackItem Item(string id, string category, bool required) =>
        new(id, id, 1, 1)
        {
            Required = required,
            SortType = category,
            SubcategoryPath = new[] { id },
        };
}
