using System;
using System.Diagnostics;
using System.Linq;
using ChouUn.InventoryOrganizer.Core.Packing;
using Xunit;
using Xunit.Abstractions;

namespace ChouUn.InventoryOrganizer.Core.Tests.Packing;

public sealed class GlobalPackerTests
{
    private readonly ITestOutputHelper _output;
    public GlobalPackerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void 各网格先完整加权再相加_不按全局父层拒绝更低总分()
    {
        PackRequest[] requests = Enumerable.Range(0, 2).Select(grid =>
            new PackRequest(1, 4, Array.Empty<FixedBlock>(),
                Enumerable.Range(0, 4).Select(i => new PackItem(
                    $"{grid}-{i}", i.ToString(), 1, 1)
                {
                    Required = true,
                    CategoryPath = grid == 0 ? new[] { "c" + i / 2 }
                        : new[] { "root", "c" + i / 2, "leaf" + i / 2 },
                }).ToArray())).ToArray();
        PackResult Layout(int grid, bool alternating) => new(
            Enumerable.Range(0, 4).Select(i => new Placement($"{grid}-{i}", 0,
                alternating ? i % 2 * 2 + i / 2 : i, false)).ToArray(),
            Array.Empty<PackItem>());
        var before = new ContainerPackResult(new[]
            { Layout(0, false), Layout(1, true) });
        var next = new ContainerPackResult(new[] { Layout(0, true), Layout(1, false) });
        for (int grid = 0; grid < requests.Length; grid++)
        {
            CpSatPackerTests.AssertValid(requests[grid], before.Grids[grid]);
            CpSatPackerTests.AssertValid(requests[grid], next.Grids[grid]);
        }

        // 网格 0 增加 2 分；网格 1 减少 2×7+2=16 分，总分改善 14。
        Assert.True(CategoryPacking.Compare(requests, next, before) < 0);
    }

    [Fact]
    public void 仓库与多个容器统一求解且归属不变()
    {
        PackRequest[] requests =
        {
            ActualStashTests.Read("hierarchy-stash.csv"),
            Rename(ActualStashTests.Read("hierarchy-junk.csv", 14, 14), "junk"),
        };
        var packer = new CachedPacker(new CpSatPacker());
        ContainerPackResult baseline = new GlobalPacker(new CpSatPacker())
            .Pack(requests, 0);
        var clock = Stopwatch.StartNew();
        ContainerPackResult result = packer.PackAll(requests, 2.6);
        _output.WriteLine($"{clock.ElapsedMilliseconds}ms {result.Diagnostic}");
        for (int i = 0; i < requests.Length; i++)
        {
            Assert.True(result.Grids[i].Complete);
            CpSatPackerTests.AssertValid(requests[i], result.Grids[i]);
            _output.WriteLine(string.Join(",", CategoryPacking.Spans(
                requests[i], result.Grids[i])));
        }
        Assert.Equal(606, CpSatPackerTests.Area(requests[0], result.Grids[0]));
        Assert.Contains("objective=grid-sum", result.Diagnostic);
        Assert.Contains("block=grid-sum", result.Diagnostic);
        Assert.Equal(61, CpSatPacker.Height(requests[0], result.Grids[0]));
        Assert.True(CategoryPacking.Span(requests[0], result.Grids[0]) <= 59);
        // 停滞退出可能早于后续改善，限时结果须保持完整分数不退步。
        Assert.True(CategoryPacking.Compare(requests, result, baseline) <= 0);
        Assert.Contains("stop=stagnation", result.Diagnostic);
        Assert.True(clock.Elapsed.TotalSeconds < 2);
        ContainerPackResult again = packer.PackAll(requests.Select((r, i) => r with
            { Current = result.Grids[i].Placements }).ToArray(), 2.6);
        Assert.Equal("global skip=unchanged-input", again.Diagnostic);
        Assert.Equal(result.Grids, again.Grids);
    }

    [Fact]
    public void 二十个容器不按数量累加求解时限()
    {
        PackRequest junk = ActualStashTests.Read("hierarchy-junk.csv", 14, 14);
        PackRequest[] requests = Enumerable.Range(0, 20)
            .Select(i => Rename(junk, "box" + i)).ToArray();
        var clock = Stopwatch.StartNew();

        ContainerPackResult result = new GlobalPacker(new CpSatPacker())
            .Pack(requests, 1);

        _output.WriteLine($"{clock.ElapsedMilliseconds}ms {result.Diagnostic}");
        Assert.True(clock.Elapsed.TotalSeconds < 6);
        Assert.Contains("grids=20", result.Diagnostic);
        Assert.Contains("active=20", result.Diagnostic);
        for (int i = 0; i < requests.Length; i++)
        {
            Assert.True(result.Grids[i].Complete);
            CpSatPackerTests.AssertValid(requests[i], result.Grids[i]);
            Assert.True(CategoryPacking.Span(requests[i], result.Grids[i]) <= 9);
        }
    }

    [Fact]
    public void 全局缓存随网格状态变化失效()
    {
        var request = new PackRequest(2, 3, Array.Empty<FixedBlock>(), new[]
        {
            new PackItem("a", "t", 1, 1) { Required = true },
        });
        var packer = new CachedPacker(new CpSatPacker());
        packer.PackAll(new[] { request }, 1);
        Assert.Contains("unchanged-input",
            packer.PackAll(new[] { request }, 1).Diagnostic);
        ContainerPackResult changed = packer.PackAll(new[] { request with
        {
            Fixed = new[] { new FixedBlock(0, 0, 1, 1) },
        } }, 1);
        Assert.DoesNotContain("unchanged-input", changed.Diagnostic);
        Assert.Equal(1, Assert.Single(changed.Grids[0].Placements).X);
    }

    private static PackRequest Rename(PackRequest request, string prefix)
        => request with
        {
            Items = request.Items.Select(i => i with { Id = prefix + ":" + i.Id })
                .ToArray(),
            Current = request.Current.Select(p => p with { Id = prefix + ":" + p.Id })
                .ToArray(),
        };
}
