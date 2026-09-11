using System;

namespace ChouUn.StashMaster.Core.Organizing;

/// <summary>收纳每次最多 1 秒、合计 2 秒；最终联合排布使用整次 3 秒的余量。</summary>
public sealed class PackBudget
{
    public double CollectionSeconds { get; private set; }
    public double FinalSeconds { get; private set; }

    public double Remaining(bool collecting) => Math.Max(0,
        collecting ? 2 - CollectionSeconds : 3 - CollectionSeconds - FinalSeconds);

    public double Available(bool collecting) => collecting
        ? Math.Min(1, Remaining(true)) : Remaining(false);

    public void Charge(bool collecting, double seconds)
    {
        // 线程调度与保底计算可能略超分配额度，不侵占另一阶段的保留额度。
        // 实际规划耗时另行完整记录，不把预算值当成墙钟耗时。
        double charged = Math.Max(0, Math.Min(seconds, Available(collecting)));
        if (collecting)
        {
            CollectionSeconds += charged;
        }
        else
        {
            FinalSeconds += charged;
        }
    }
}
