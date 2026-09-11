using System;
using System.Linq;
using ChouUn.StashMaster.Core.Packing;
using Xunit;

namespace ChouUn.StashMaster.Core.Tests.Packing;

public sealed class HierarchyPackingTests
{
    [Fact]
    public void 父类优先_枪械整体集中再聚合枪械子类()
    {
        var request = new PackRequest(2, 4, Array.Empty<FixedBlock>(), new[]
        {
            Item("0", "weapon", "rifle"), Item("1", "gear", "bag"),
            Item("2", "weapon", "shotgun"), Item("3", "gear", "rig"),
            Item("4", "weapon", "rifle"), Item("5", "gear", "bag"),
            Item("6", "weapon", "shotgun"), Item("7", "gear", "rig"),
        });

        PackResult result = new CpSatPacker().Pack(request);

        Assert.Equal(new[] { 2, 0 }, CategoryPacking.Spans(request, result));
        CpSatPackerTests.AssertValid(request, result);
    }

    [Fact]
    public void 子类跨度更好也不能替换父类更好的当前布局()
    {
        var request = new PackRequest(2, 4, Array.Empty<FixedBlock>(), new[]
        {
            Item("0", "weapon", "rifle"), Item("1", "weapon", "rifle"),
            Item("2", "gear", "bag"), Item("3", "gear", "bag"),
            Item("4", "weapon", "shotgun"), Item("5", "weapon", "shotgun"),
            Item("6", "gear", "rig"), Item("7", "gear", "rig"),
        })
        {
            Current = new[] { "0", "4", "1", "5", "2", "6", "3", "7" }
                .Select((id, n) => new Placement(id, n % 2, n / 2, false)).ToArray(),
        };
        PackResult fresh = new HeuristicPacker().Pack(request);
        var current = new PackResult(request.Current, Array.Empty<PackItem>());
        Assert.Equal(new[] { 4, 0 }, CategoryPacking.Spans(request, fresh));
        Assert.Equal(new[] { 2, 4 }, CategoryPacking.Spans(request, current));

        PackResult result = new CpSatPacker().Pack(request, 0);

        Assert.Equal(request.Current, result.Placements);
    }

    [Fact]
    public void 收纳多网格面积已满_不为父层聚合改变布局()
    {
        var request = new PackRequest(2, 2, Array.Empty<FixedBlock>(), new[]
        {
            Item("0", "weapon", "rifle"), Item("1", "gear", "bag"),
            Item("2", "weapon", "shotgun"), Item("3", "gear", "rig"),
        });
        var shallow = new PackRequest(1, 2, Array.Empty<FixedBlock>(),
            new[] { Item("money", "money"), Item("other", "unknown") });

        ContainerPackResult result = new ContainerPacker(new CpSatPacker())
            .Pack(new[] { request, shallow }, 1);

        Assert.Equal(new[] { 2, 0 }, CategoryPacking.Spans(request, result.Grids[0]));
        Assert.Equal(new[] { 0 }, CategoryPacking.Spans(shallow, result.Grids[1]));
        Assert.Contains("skip=area-bound", result.Diagnostic);
    }

    [Fact]
    public void 非单格可选物品也能由通用界限证明已最优()
    {
        var request = new PackRequest(4, 2, Array.Empty<FixedBlock>(),
            Enumerable.Range(0, 3).Select(n =>
                Item(n.ToString(), "weapon", "rifle") with
                { Width = 2, Height = 2, Required = false }).ToArray());

        PackResult result = new CpSatPacker().Pack(request);

        Assert.Equal(2, result.Placements.Count);
        Assert.Equal(new[] { 1, 1 }, CategoryPacking.Spans(request, result));
        Assert.Contains("skip=optimum-bound", result.Diagnostic);
        CpSatPackerTests.AssertValid(request, result);
    }

    private static PackItem Item(string id, params string[] path) =>
        new(id, id, 1, 1) { Required = true, CategoryPath = path };
}
