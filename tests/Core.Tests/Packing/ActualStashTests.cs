using System;
using System.IO;
using System.Linq;
using ChouUn.InventoryOrganizer.Core.Packing;
using Xunit;
using Xunit.Abstractions;

namespace ChouUn.InventoryOrganizer.Core.Tests.Packing;

public sealed class ActualStashTests
{
    private readonly ITestOutputHelper _output;

    public ActualStashTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void 刚整理后的真实仓库继续改善父类聚合且空间不退步()
    {
        PackRequest request = Read("hierarchy-stash.csv");
        var before = new PackResult(request.Current, Array.Empty<PackItem>());
        var packer = new CachedPacker(new CpSatPacker());
        Assert.Equal(new[] { 195, 256, 117, 39 },
            CategoryPacking.Spans(request, before));

        PackResult result = packer.Pack(request, 1);

        _output.WriteLine(result.Diagnostic);
        _output.WriteLine("before=" + string.Join(",", CategoryPacking.Spans(
            request, before)) + "; after=" + string.Join(",",
                CategoryPacking.Spans(request, result)));
        foreach (string category in new[] { "21", "22", "70" })
        {
            PackRequest group = request with
            {
                Items = request.Items.Where(i => i.CategoryPath.Count > 0
                    && i.CategoryPath[0] == category).ToArray(),
            };
            var ids = group.Items.Select(i => i.Id).ToArray();
            _output.WriteLine($"parent {category}: "
                + CategoryPacking.Span(group, before with
                {
                    Placements = before.Placements.Where(p => ids.Contains(p.Id))
                        .ToArray(),
                })
                + " -> " + CategoryPacking.Span(group, result with
                {
                    Placements = result.Placements.Where(p => ids.Contains(p.Id))
                        .ToArray(),
                }));
        }
        Assert.True(result.Complete);
        Assert.Equal(606, CpSatPackerTests.Area(request, result));
        Assert.Equal(61, Height(request, result));
        Assert.True(CategoryPacking.Span(request, result) <= 59, result.Diagnostic);
        CpSatPackerTests.AssertValid(request, result);
        PackResult again = packer.Pack(request with { Current = result.Placements });
        Assert.Contains("skip=unchanged-input", again.Diagnostic);
        Assert.Equal(result.Placements, again.Placements);
    }

    [Fact]
    public void 当前仓库在一秒预算内实质改善类别聚合()
    {
        PackRequest request = Read();
        var before = new PackResult(request.Current, Array.Empty<PackItem>());
        Assert.Equal(322, request.Items.Count);
        Assert.Equal(606, CpSatPackerTests.Area(request, before));
        Assert.Equal(61, Height(request, before));
        Assert.Equal(677, CategoryPacking.Span(request, before));
        CpSatPackerTests.AssertValid(request, before);

        var packer = new CachedPacker(new CpSatPacker());
        PackResult result = packer.Pack(request, 1);

        _output.WriteLine(result.Diagnostic);
        CpSatPackerTests.AssertValid(request, result);
        Assert.True(result.Complete);
        Assert.Equal(61, Height(request, result));
        Assert.True(CategoryPacking.Span(request, result) <= 247, result.Diagnostic);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        PackResult again = packer.Pack(request with { Current = result.Placements }, 1);
        Assert.Contains("skip=unchanged-input", again.Diagnostic);
        Assert.Equal(result.Placements, again.Placements);
        Assert.True(elapsed.ElapsedMilliseconds < 100);
        _output.WriteLine($"unchanged repeat: {elapsed.ElapsedMilliseconds} ms");
    }

    private static int Height(PackRequest request, PackResult result) =>
        result.Placements.Max(p => p.Y + (p.Rotated
            ? request.Items.Single(i => i.Id == p.Id).Width
            : request.Items.Single(i => i.Id == p.Id).Height));

    // 存档位置 + 运行时模组模板 + 游戏/Foldables 尺寸公式，匿名化身份与分类。
    private static PackRequest Read(string fixture = "current-stash.csv")
    {
        using Stream stream = typeof(ActualStashTests).Assembly
            .GetManifestResourceStream(typeof(ActualStashTests).Namespace
                + ".Fixtures." + fixture)!;
        using var reader = new StreamReader(stream);
        string[][] data = reader.ReadToEnd().Split(new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(',')).ToArray();
        var paths = data.ToDictionary(row => row[0], row => row[2] == "-1"
            ? Array.Empty<string>() : row[2].Split('/'));
        int[][] rows = data.Select(row => row.Select((value, index) =>
            index == 2 ? 0 : int.Parse(value)).ToArray()).ToArray();
        return new PackRequest(10, 72, rows.Where(r => r[8] != 0)
            .Select(r => new FixedBlock(r[5], r[6],
                r[7] != 0 ? r[4] : r[3], r[7] != 0 ? r[3] : r[4])).ToArray(),
            rows.Where(r => r[8] == 0).Select(r =>
                new PackItem(r[0].ToString(), r[1].ToString("D3"), r[3], r[4])
                {
                    Required = true,
                    CategoryPath = paths[r[0].ToString()],
                }).ToArray())
        {
            Current = rows.Where(r => r[8] == 0).Select(r =>
                new Placement(r[0].ToString(), r[5], r[6], r[7] != 0)).ToArray(),
        };
    }
}
