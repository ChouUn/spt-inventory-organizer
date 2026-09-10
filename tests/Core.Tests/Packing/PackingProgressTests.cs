using System;
using System.Diagnostics;
using System.Threading;
using ChouUn.InventoryOrganizer.Core.Packing;
using Xunit;

namespace ChouUn.InventoryOrganizer.Core.Tests.Packing;

public sealed class PackingProgressTests
{
    private static readonly PackRequest Request = new(1, 4,
        Array.Empty<FixedBlock>(), new[]
        {
            new PackItem("a", "a", 1, 1) { Required = true },
        });

    [Fact]
    public void 预处理不计时_首次可行解后没有回调也能停止()
    {
        using var stopped = new ManualResetEventSlim();
        using var progress = Create(stopped);
        Assert.False(stopped.Wait(600));

        var clock = Stopwatch.StartNew();
        progress.Observe(Layout(3));

        Assert.True(stopped.Wait(1500));
        Assert.True(clock.ElapsedMilliseconds >= 490);
        Assert.True(progress.Stopped);
        Assert.Equal(0, progress.Improvements);
        // 原生搜索响应停止请求前仍可能交付一个更好的解，必须保留。
        ContainerPackResult last = Layout(1);
        progress.Observe(last);
        Assert.Same(last, progress.Best);
    }

    [Fact]
    public void 严格改善重置计时_同分与更差解不延后退出()
    {
        using var stopped = new ManualResetEventSlim();
        using var progress = Create(stopped);
        progress.Observe(Layout(3));
        Assert.False(stopped.Wait(300));
        ContainerPackResult better = Layout(2);
        progress.Observe(better);
        Assert.False(stopped.Wait(300));
        progress.Observe(Layout(2));
        progress.Observe(Layout(3));

        Assert.True(stopped.Wait(400));
        Assert.Same(better, progress.Best);
        Assert.Equal(1, progress.Improvements);
    }

    [Fact]
    public void 分段预处理期间暂停_新段同分解不重置已累计停滞()
    {
        using var stopped = new ManualResetEventSlim();
        using var progress = Create(stopped);
        progress.Observe(Layout(3));
        Assert.False(stopped.Wait(300));
        progress.EndSearch();
        Assert.False(stopped.Wait(600));
        progress.Observe(Layout(3));

        Assert.True(stopped.Wait(400));
    }

    [Fact]
    public void 求解结束或释放后不再调用停止接口()
    {
        using var stopped = new ManualResetEventSlim();
        using (var progress = Create(stopped))
        {
            progress.Observe(Layout(3));
            progress.EndSearch();
            Assert.False(stopped.Wait(600));
            progress.Observe(Layout(3));
        }
        Assert.False(stopped.Wait(600));
    }

    private static PackingProgress Create(ManualResetEventSlim stopped) =>
        new(new[] { Request }, Layout(3), _ => Layout(3), stopped.Set);

    private static ContainerPackResult Layout(int row) => new(new[]
    {
        new PackResult(new[] { new Placement("a", 0, row, false) },
            Array.Empty<PackItem>()),
    });
}
