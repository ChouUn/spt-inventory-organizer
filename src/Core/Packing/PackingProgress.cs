using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using Google.OrTools.Sat;

namespace ChouUn.StashMaster.Core.Packing;

/// <summary>完整布局连续 0.5 秒无改善即停搜；首次可行解前不计停滞时间。</summary>
internal sealed class PackingProgress : IDisposable
{
    private const double IdleMilliseconds = 500;
    private readonly object _gate = new();
    private readonly IReadOnlyList<PackRequest> _requests;
    private readonly Action _stop;
    private readonly Timer _timer;
    private readonly Stopwatch _idle = new();
    private readonly SolutionObserver _callback;
    private BigInteger _score;
    private bool _searching;
    private bool _disposed;

    public PackingProgress(IReadOnlyList<PackRequest> requests,
        ContainerPackResult baseline,
        Func<CpSolverSolutionCallback, ContainerPackResult> read, Action stop)
    {
        _requests = requests;
        Best = baseline;
        _score = Score(baseline);
        _stop = stop;
        _timer = new Timer(Check, null, Timeout.Infinite, Timeout.Infinite);
        _callback = new SolutionObserver(this, read);
    }

    public CpSolverSolutionCallback Callback => _callback;
    public ContainerPackResult Best { get; private set; }
    public bool Stopped { get; private set; }
    public int Improvements { get; private set; }

    public string Diagnostic => (Stopped ? "stop=stagnation; " : "")
        + $"idle-limit=500ms, idle={_idle.ElapsedMilliseconds}ms, "
        + $"improvements={Improvements}";

    /// <summary>按实际位置计算完整分数，不把模型辅助变量或下界变动视为改善。</summary>
    internal void Observe(ContainerPackResult result)
    {
        BigInteger score = Score(result);
        lock (_gate)
        {
            if (_disposed) { return; }
            bool improved = score < _score;
            if (improved)
            {
                Best = result;
                _score = score;
                Improvements++;
            }
            if (Stopped) { return; }
            if (improved) { _idle.Restart(); }
            else { _idle.Start(); }
            _searching = true;
            Schedule();
        }
    }

    /// <summary>暂停计时；分段之间的结果处理和下一段预处理不算搜索停滞。</summary>
    public void EndSearch()
    {
        lock (_gate)
        {
            _searching = false;
            _idle.Stop();
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private BigInteger Score(ContainerPackResult result) => _requests
        .Select((request, grid) => CategoryPacking.Score(request, result.Grids[grid]))
        .Aggregate(BigInteger.Zero, (sum, score) => sum + score);

    private void Schedule()
    {
        double remaining = IdleMilliseconds - _idle.Elapsed.TotalMilliseconds;
        _timer.Change((int)Math.Max(1, Math.Ceiling(remaining)), Timeout.Infinite);
    }

    private void Check(object? state)
    {
        lock (_gate)
        {
            if (_disposed || !_searching || Stopped) { return; }
            if (_idle.Elapsed.TotalMilliseconds < IdleMilliseconds)
            {
                Schedule();
                return;
            }
            Stopped = true;
            _idle.Stop();
            _stop();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _searching = false;
            _idle.Stop();
            _timer.Dispose();
        }
        // 锁保证没有 StopSearch 调用仍在运行，随后才释放回调和求解器。
        _callback.Dispose();
    }

    private sealed class SolutionObserver : CpSolverSolutionCallback
    {
        private readonly PackingProgress _owner;
        private readonly Func<CpSolverSolutionCallback, ContainerPackResult> _read;

        public SolutionObserver(PackingProgress owner,
            Func<CpSolverSolutionCallback, ContainerPackResult> read)
        {
            _owner = owner;
            _read = read;
        }

        public override void OnSolutionCallback() => _owner.Observe(_read(this));
    }
}
