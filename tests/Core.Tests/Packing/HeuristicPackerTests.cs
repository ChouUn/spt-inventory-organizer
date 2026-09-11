using System;
using System.Linq;
using ChouUn.StashMaster.Core.Packing;
using Xunit;

namespace ChouUn.StashMaster.Core.Tests.Packing;

public sealed class HeuristicPackerTests
{
    private static readonly HeuristicPacker Packer = new HeuristicPacker();

    [Fact]
    public void 大件先放_同模板相邻_不重叠不越界()
    {
        var request = new PackRequest(4, 3, Array.Empty<FixedBlock>(), new[]
        {
            new PackItem("s1", "small", 1, 1),
            new PackItem("big", "big", 2, 2),
            new PackItem("s2", "small", 1, 1),
            new PackItem("bar", "bar", 3, 1),
        });

        PackResult result = Packer.Pack(request);

        Assert.True(result.Complete);
        Assert.Equal(
            new[] { "big", "bar", "s1", "s2" }, result.Placements.Select(p => p.Id));
        AssertNoOverlapWithinBounds(request, result);
    }

    [Fact]
    public void 需要时转置()
    {
        var request = new PackRequest(1, 3, Array.Empty<FixedBlock>(), new[]
        {
            new PackItem("bar", "bar", 3, 1),
        });

        Placement placement = Assert.Single(Packer.Pack(request).Placements);

        Assert.True(placement.Rotated);
        Assert.Equal((0, 0), (placement.X, placement.Y));
    }

    [Fact]
    public void 绕开固定占位()
    {
        var request = new PackRequest(2, 2, new[] { new FixedBlock(0, 0, 1, 2) }, new[]
        {
            new PackItem("a", "t", 1, 2),
        });

        Placement placement = Assert.Single(Packer.Pack(request).Placements);

        Assert.Equal((1, 0, false), (placement.X, placement.Y, placement.Rotated));
    }

    [Fact]
    public void 放不下的物品原样返回()
    {
        var request = new PackRequest(2, 2, Array.Empty<FixedBlock>(), new[]
        {
            new PackItem("a", "t", 2, 2),
            new PackItem("b", "t", 1, 1),
        });

        PackResult result = Packer.Pack(request);

        Assert.False(result.Complete);
        Assert.Equal("b", Assert.Single(result.Unplaced).Id);
    }

    private static void AssertNoOverlapWithinBounds(
        PackRequest request, PackResult result)
    {
        var cells = new bool[request.Width, request.Height];
        foreach (Placement p in result.Placements)
        {
            PackItem item = request.Items.Single(i => i.Id == p.Id);
            int w = p.Rotated ? item.Height : item.Width;
            int h = p.Rotated ? item.Width : item.Height;
            for (int x = p.X; x < p.X + w; x++)
            {
                for (int y = p.Y; y < p.Y + h; y++)
                {
                    Assert.False(cells[x, y], $"{p.Id} overlaps at ({x},{y})");
                    cells[x, y] = true;
                }
            }
        }
    }
}
