using System;
using ChouUn.StashMaster.Core.Packing;
using Xunit;

namespace ChouUn.StashMaster.Core.Tests.Packing;

public sealed class CachedPackerTests
{
    [Fact]
    public void 类别链按内容复用_只变父类也必须重新求解()
    {
        var inner = new CountingPacker();
        var packer = new CachedPacker(inner);
        var item = new PackItem("a", "t", 1, 1)
        {
            Required = true,
            SortType = "Food",
            SubcategoryPath = new[] { "prepared", "canned" },
        };
        var request = new PackRequest(1, 1, Array.Empty<FixedBlock>(), new[] { item });
        packer.Pack(request);
        packer.Pack(request with
        {
            Items = new[] { item with { SubcategoryPath = new[] { "prepared", "canned" } } },
        });
        Assert.Equal(1, inner.Calls);
        packer.Pack(request with
        {
            Items = new[] { item with { SubcategoryPath = new[] { "preserved", "canned" } } },
        });
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public void 相同输入与已应用结果复用_位置类别尺寸锁和预算变化重新计算()
    {
        var inner = new CountingPacker();
        var packer = new CachedPacker(inner);
        var request = new PackRequest(3, 3, Array.Empty<FixedBlock>(),
            new[] { new PackItem("a", "t", 1, 1) { Required = true, SortType = "Food" } })
        {
            Current = new[] { new Placement("a", 2, 2, false) },
        };
        PackResult result = packer.Pack(request, 0.5);
        packer.Pack(request, 0.5);
        packer.Pack(request with { Current = result.Placements }, 0.5);
        Assert.Equal(1, inner.Calls);
        packer.Pack(request, 1);
        packer.Pack(request with
        {
            Current = new[] { new Placement("a", 1, 2, false) },
        });
        packer.Pack(request with
        {
            Items = new[]
            {
                request.Items[0] with { SubcategoryPath = new[] { "prepared", "canned" } },
            },
        });
        packer.Pack(request with
        {
            Items = new[] { request.Items[0] with { Height = 2 } },
        });
        packer.Pack(request with { Fixed = new[] { new FixedBlock(0, 0, 1, 1) } });
        Assert.Equal(6, inner.Calls);
    }

    private sealed class CountingPacker : IPacker
    {
        public int Calls { get; private set; }

        public PackResult Pack(PackRequest request, double maxSeconds = 1)
        {
            Calls++;
            return new HeuristicPacker().Pack(request);
        }
    }
}
