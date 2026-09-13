using System;
using System.Linq;
using ChouUn.StashMaster.Core.Packing;
using Xunit;

namespace ChouUn.StashMaster.Core.Tests.Packing;

public sealed class PackingTreeTests
{
    [Fact]
    public void 同名类别按完整前缀分组_不同父分支不能误合并()
    {
        PackRequest request = Grid(
            Item("left-leaf", "Barter", "left", "shared"),
            Item("left-direct", "Barter", "left"),
            Item("right-leaf", "Barter", "right", "shared"),
            Item("right-direct", "Barter", "right"));
        PackingTree tree = PackingTree.For(request);
        PackingTree.Node[] leaves = tree.Nodes.Where(n => n.CategoryId == "shared").ToArray();

        Assert.Equal(new[] { "left:left-leaf", "right:right-leaf" }, leaves
            .Select(n => n.Parent!.CategoryId + ":" + Members(n)).OrderBy(s => s));
        Assert.NotEqual(leaves[0].Key, leaves[1].Key);
        Assert.All(leaves, n => Assert.Equal(2, n.Depth));
        Assert.Equal(new[] { 3, 2, 0 }, CategoryPacking.Spans(request,
            Layout("left-direct", "left-leaf", "right-direct", "right-leaf")));
    }

    [Fact]
    public void 等成员链压缩_直接成员使有效子分支保留_按本网格重新裁剪()
    {
        PackRequest request = Grid(
            Item("direct", "Barter", "common"),
            Item("a", "Barter", "common", "branch", "wrapper", "a"),
            Item("b", "Barter", "common", "branch", "wrapper", "b"));
        PackingTree tree = PackingTree.For(request);

        Assert.Equal("a,b,direct", Members(Assert.Single(tree.Roots)));
        PackingTree.Node branch = Assert.Single(tree.Nodes, n => n.CategoryId == "branch");
        Assert.Equal("a,b", Members(branch));
        Assert.Equal(1, branch.Depth);
        Assert.Equal(new string?[] { null, "branch", "a" },
            tree.Paths["a"].Select(n => n.CategoryId));
        Assert.Single(tree.Paths["direct"]);
        Assert.DoesNotContain(tree.Nodes, n => n.CategoryId == "common" || n.CategoryId == "wrapper");

        Assert.Equal(new[] { 2, 1, 0 }, CategoryPacking.Spans(request, Layout("direct", "a", "b")));
        PackRequest trimmed = request with { Items = request.Items.Where(i => i.Id != "direct").ToArray() };
        PackingTree trimmedTree = PackingTree.For(trimmed);
        Assert.Equal("a,b", Members(Assert.Single(trimmedTree.Roots)));
        Assert.Equal(new string?[] { null, "a" }, trimmedTree.Paths["a"].Select(n => n.CategoryId));
        Assert.Equal(new[] { 1, 0 }, CategoryPacking.Spans(trimmed, Layout("a", "b")));
        Assert.Equal(Signature(tree), Signature(PackingTree.For(request with
        {
            Current = new[] { new Placement("direct", 0, 0, false) },
        })));
    }

    [Fact]
    public void 空子链直接进入排序根_空排序类型只有一个未识别根()
    {
        PackRequest request = Grid(
            Item("known-a", "Armor") with { TemplateId = "template-a" },
            Item("known-b", "Armor") with { TemplateId = "template-b" },
            Item("unknown-a", ""),
            Item("unknown-b", ""), Item("unknown-empty", ""));
        PackingTree tree = PackingTree.For(request);

        Assert.Equal("known-a,known-b", Members(Assert.Single(tree.Roots, n => n.SortType == "Armor")));
        Assert.Equal("unknown-a,unknown-b,unknown-empty", Members(Assert.Single(tree.Roots, n => n.SortType == "")));
        Assert.All(tree.Paths.Values, path => Assert.Single(path));
        Assert.Equal(new[] { 6 }, CategoryPacking.Spans(request,
            Layout("known-a", "unknown-a", "unknown-b", "known-b", "unknown-empty")));
    }

    [Fact]
    public void 关闭配置只关闭先后约束_仍使用相同排序树聚合()
    {
        PackRequest request = Grid(
            Item("armor-a", "Armor", "a"),
            Item("armor-b", "Armor", "b"),
            Item("ammo-a", "Ammo", "a"),
            Item("ammo-b", "Ammo", "b")) with
        {
            CategoryOrder = new[] { "Armor", "Ammo" },
        };
        PackRequest disabled = request with { CategoryOrder = Array.Empty<string>() };
        PackResult result = Layout("ammo-a", "ammo-b", "armor-a", "armor-b");

        Assert.Equal(Signature(PackingTree.For(request)), Signature(PackingTree.For(disabled)));
        Assert.Equal(new[] { "Armor", "Ammo" }, PackingTree.For(request).Roots.Select(n => n.SortType));
        Assert.Equal(CategoryPacking.Spans(request, result), CategoryPacking.Spans(disabled, result));
        Assert.Equal(CategoryPacking.Score(request, result), CategoryPacking.Score(disabled, result));
        Assert.True(CategoryOrder.Penalty(request, result) > 0);
        Assert.Empty(CategoryOrder.Pairs(disabled));
        Assert.Equal(0, CategoryOrder.Penalty(disabled, result));
    }

    [Fact]
    public void 顶层顺序使用覆盖中线而非硬分区_与跨度使用同一成员范围()
    {
        PackRequest request = Grid(Item("a-top", "A"), Item("a-bottom", "A"),
            Item("b-top", "B"), Item("b-bottom", "B")) with
        {
            Width = 2,
            CategoryOrder = new[] { "A", "B" },
        };
        var result = new PackResult(new[]
        {
            new Placement("a-top", 0, 0, false), new Placement("a-bottom", 0, 3, false),
            new Placement("b-top", 1, 1, false), new Placement("b-bottom", 1, 2, false),
        }, Array.Empty<PackItem>());

        CpSatPackerTests.AssertValid(request, result);
        Assert.Equal(0, CategoryOrder.Penalty(request, result));
        Assert.Equal(new[] { 4 }, CategoryPacking.Spans(request, result));
        PackResult reversed = result with
        {
            Placements = result.Placements.Select(p => p.Id.StartsWith("b-") ? p with { Y = p.Y - 1 } : p).ToArray(),
        };
        CpSatPackerTests.AssertValid(request, reversed);
        Assert.True(CategoryOrder.Penalty(request, reversed) > 0);
        Assert.Equal(CategoryPacking.Spans(request, result), CategoryPacking.Spans(request, reversed));
    }

    private static PackItem Item(string id, string sortType, params string[] subcategoryPath) =>
        new(id, "same-template", 1, 1)
        {
            Required = true,
            SortType = sortType,
            SubcategoryPath = subcategoryPath,
        };

    private static PackRequest Grid(params PackItem[] items) =>
        new(1, items.Length, Array.Empty<FixedBlock>(), items);

    private static PackResult Layout(params string[] ids) => new(
        ids.Select((id, row) => new Placement(id, 0, row, false)).ToArray(),
        Array.Empty<PackItem>());

    private static string Members(PackingTree.Node node) =>
        string.Join(",", node.Items.Select(i => i.Id).OrderBy(id => id));

    private static string[] Signature(PackingTree tree) => tree.Paths
        .Select(pair => pair.Key + ":" + string.Join("/", pair.Value.Select(n =>
            n.SortType + ":" + n.CategoryId + ":" + n.Depth + ":" + Members(n))))
        .OrderBy(s => s).ToArray();
}
