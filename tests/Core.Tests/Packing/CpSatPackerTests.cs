using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ChouUn.InventoryOrganizer.Core.Packing;
using Xunit;

namespace ChouUn.InventoryOrganizer.Core.Tests.Packing;

public sealed class CpSatPackerTests
{
    [Fact]
    public void 重排大件解决启发式留下的碎片空位()
    {
        var request = new PackRequest(4, 4, Array.Empty<FixedBlock>(), new[]
        {
            new PackItem("bar", "t", 1, 4),
            new PackItem("square", "t", 2, 2),
            new PackItem("large", "t", 2, 3),
            new PackItem("single1", "t", 1, 1),
            new PackItem("single2", "t", 1, 1),
        });

        Assert.False(new HeuristicPacker().Pack(request).Complete);
        PackResult result = new CpSatPacker().Pack(request);

        Assert.True(result.Complete);
        AssertValid(request, result);
    }

    [Fact]
    public void 空间不足先最大化面积_再压低占用行数()
    {
        var request = new PackRequest(3, 3, Array.Empty<FixedBlock>(), new[]
        {
            new PackItem("wide", "t", 2, 2),
            new PackItem("bar1", "t", 1, 3),
            new PackItem("bar2", "t", 1, 3),
            new PackItem("bar3", "t", 1, 3),
        });

        PackResult result = new CpSatPacker().Pack(request);

        Assert.Equal(9, Area(request, result));
        Assert.Equal("wide", Assert.Single(result.Unplaced).Id);
        AssertValid(request, result);
    }

    [Fact]
    public void 转置并绕开固定物品_单格填缝()
    {
        var request = new PackRequest(3, 5,
            new[] { new FixedBlock(0, 0, 1, 3) }, new[]
            {
                new PackItem("bar", "t", 5, 1),
                new PackItem("square", "t", 2, 2),
                new PackItem("single", "t", 1, 1),
            });

        PackResult result = new CpSatPacker().Pack(request);

        Assert.True(result.Complete);
        AssertValid(request, result);
        Assert.True(result.Placements.Single(p => p.Id == "bar").Rotated);
    }

    [Fact]
    public void 同等紧凑仍执行常规排序_再次整理布局稳定()
    {
        var request = new PackRequest(4, 2, Array.Empty<FixedBlock>(), new[]
        {
            new PackItem("a", "t", 2, 1),
            new PackItem("b", "t", 2, 1),
        })
        {
            Current = new[]
            {
                new Placement("a", 2, 0, false),
                new Placement("b", 0, 0, false),
            },
        };

        PackResult result = new CpSatPacker().Pack(request, maxSeconds: 0);

        Assert.Equal(new HeuristicPacker().Pack(request).Placements, result.Placements);
        Assert.NotEqual(request.Current, result.Placements);
        PackResult repeated = new CpSatPacker().Pack(
            request with { Current = result.Placements }, maxSeconds: 0);
        Assert.Equal(result.Placements, repeated.Placements);
        AssertValid(request, result);
    }

    [Fact]
    public void 底部固定物品不应阻止上方散放物品归拢()
    {
        var request = new PackRequest(10, 72,
            new[] { new FixedBlock(0, 71, 1, 1) }, new[]
            {
                new PackItem("a", "t", 2, 2) { Required = true },
                new PackItem("b", "t", 1, 1) { Required = true },
            })
        {
            Current = new[]
            {
                new Placement("a", 5, 40, false),
                new Placement("b", 8, 60, false),
            },
        };

        PackResult result = new CpSatPacker().Pack(request);

        Assert.Equal(72, Height(request, result));
        Assert.All(result.Placements, p => Assert.Equal(0, p.Y));
        AssertValid(request, result);
    }

    [Fact]
    public void 底部固定障碍不应让仓库跳过大件求解()
    {
        var request = new PackRequest(4, 8,
            new[] { new FixedBlock(0, 7, 4, 1) }, new[]
            {
                new PackItem("bar", "t", 1, 4),
                new PackItem("square", "t", 2, 2),
                new PackItem("large", "t", 2, 3),
                new PackItem("single1", "t", 1, 1),
                new PackItem("single2", "t", 1, 1),
            });

        PackResult result = new CpSatPacker().Pack(request);

        Assert.Contains("cp-sat Optimal", result.Diagnostic);
        Assert.True(result.Complete);
        Assert.All(result.Placements, p =>
        {
            PackItem item = request.Items.Single(i => i.Id == p.Id);
            Assert.True(p.Y + (p.Rotated ? item.Width : item.Height) <= 4);
        });
        AssertValid(request, result);
    }

    [Fact]
    public void 纯单格直接填空_不启动求解器()
    {
        var request = new PackRequest(10, 72, Array.Empty<FixedBlock>(),
            Enumerable.Range(0, 360).Select(i =>
                new PackItem(i.ToString(), "t", 1, 1)).ToArray());

        PackResult result = new CpSatPacker().Pack(request);

        Assert.True(result.Complete);
        Assert.Equal(36, Height(request, result));
        Assert.DoesNotContain("cp-sat", result.Diagnostic);
        AssertValid(request, result);
    }

    [Fact]
    public void 高度以内的固定障碍必须扣面积_单格不能放到目标高度之外()
    {
        var request = new PackRequest(4, 6,
            new[] { new FixedBlock(0, 1, 2, 4) }, new[]
            {
                new PackItem("large", "t", 2, 3),
                new PackItem("a", "t", 1, 1),
                new PackItem("b", "t", 1, 1),
                new PackItem("c", "t", 1, 1),
                new PackItem("d", "t", 1, 1),
                new PackItem("e", "t", 1, 1),
            });

        PackResult result = new CpSatPacker().Pack(request);

        Assert.True(result.Complete);
        AssertValid(request, result);
        int movableBottom = result.Placements.Max(p =>
        {
            PackItem item = request.Items.Single(i => i.Id == p.Id);
            return p.Y + (p.Rotated ? item.Width : item.Height);
        });
        Assert.Equal(5, movableBottom);
    }

    [Fact]
    public void 日志明确区分预算耗尽与理论最优_保留定位数值()
    {
        var request = new PackRequest(4, 8,
            new[] { new FixedBlock(0, 7, 4, 1) }, new[]
            {
                new PackItem("a", "t", 4, 1),
            });

        PackResult noBudget = new CpSatPacker().Pack(request, maxSeconds: 0);
        PackResult optimum = new CpSatPacker().Pack(request);

        Assert.Contains("skip=budget-exhausted", noBudget.Diagnostic);
        Assert.Contains("skip=optimum-bound", optimum.Diagnostic);
        Assert.Contains("fixed-bottom=8", optimum.Diagnostic);
        Assert.Contains("baseline-movable-rows=1", optimum.Diagnostic);
        Assert.Contains("budget=1.000s", optimum.Diagnostic);
    }

    [Fact]
    public void 多种尺寸与固定障碍下不劣于启发式()
    {
        var random = new Random(9);
        for (int sample = 0; sample < 12; sample++)
        {
            var request = new PackRequest(6, 6,
                new[] { new FixedBlock(0, 0, 1, 2) },
                Enumerable.Range(0, 12).Select(i => new PackItem(
                    i.ToString(), "t", random.Next(1, 4), random.Next(1, 4)))
                    .ToArray());
            PackResult baseline = new HeuristicPacker().Pack(request);
            PackResult result = new CpSatPacker().Pack(request, maxSeconds: 0.1);

            Assert.True(Area(request, result) >= Area(request, baseline));
            if (Area(request, result) == Area(request, baseline))
            {
                Assert.True(Height(request, result) <= Height(request, baseline));
            }
            AssertValid(request, result);
        }
    }

    [Fact]
    public void 大仓库短时限返回合法保底_不会无限搜索()
    {
        var request = new PackRequest(10, 72,
            new[] { new FixedBlock(4, 7, 2, 3), new FixedBlock(0, 25, 3, 2) },
            Enumerable.Range(0, 360).Select(i => new PackItem(i.ToString(), "t",
                i < 240 ? 1 : 2 + i % 3, i < 240 ? 1 : 1 + i % 3)).ToArray());
        PackResult baseline = new HeuristicPacker().Pack(request);
        var elapsed = Stopwatch.StartNew();

        PackResult result = new CpSatPacker().Pack(request, maxSeconds: 0.05);

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2));
        Assert.True(Area(request, result) >= Area(request, baseline));
        AssertValid(request, result);
    }

    [Fact]
    public void 超大可选物品不能使可放的小件无解()
    {
        var request = new PackRequest(2, 2, Array.Empty<FixedBlock>(), new[]
        {
            new PackItem("tooLarge", "t", 4, 4),
            new PackItem("fits", "t", 2, 2),
        });

        PackResult result = new CpSatPacker().Pack(request);

        Assert.Equal("fits", Assert.Single(result.Placements).Id);
        Assert.Equal("tooLarge", Assert.Single(result.Unplaced).Id);
        AssertValid(request, result);
    }

    internal static int Area(PackRequest request, PackResult result) =>
        result.Placements.Sum(p =>
        {
            PackItem item = request.Items.Single(i => i.Id == p.Id);
            return item.Width * item.Height;
        });

    internal static int Height(PackRequest request, PackResult result) =>
        result.Placements.Select(p =>
        {
            PackItem item = request.Items.Single(i => i.Id == p.Id);
            return p.Y + (p.Rotated ? item.Width : item.Height);
        }).Concat(request.Fixed.Select(f => f.Y + f.Height)).DefaultIfEmpty().Max();

    internal static void AssertValid(PackRequest request, PackResult result)
    {
        var occupied = new HashSet<(int X, int Y)>();
        foreach (FixedBlock block in request.Fixed)
        {
            Mark(block.X, block.Y, block.Width, block.Height);
        }
        Assert.Equal(request.Items.Count,
            result.Placements.Count + result.Unplaced.Count);
        Assert.Equal(request.Items.Select(i => i.Id).OrderBy(id => id),
            result.Placements.Select(p => p.Id)
                .Concat(result.Unplaced.Select(i => i.Id)).OrderBy(id => id));
        foreach (Placement p in result.Placements)
        {
            PackItem item = request.Items.Single(i => i.Id == p.Id);
            Mark(p.X, p.Y, p.Rotated ? item.Height : item.Width,
                p.Rotated ? item.Width : item.Height);
        }

        void Mark(int x, int y, int width, int height)
        {
            Assert.InRange(x, 0, request.Width - width);
            Assert.InRange(y, 0, request.Height - height);
            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    Assert.True(occupied.Add((x + dx, y + dy)), "物品或障碍重叠");
                }
            }
        }
    }
}
