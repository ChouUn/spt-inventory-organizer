using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Google.OrTools.Sat;

namespace ChouUn.InventoryOrganizer.Core.Packing;

/// <summary>网格内按优先级加权，再累加；超大总分按进位精确拆分。</summary>
internal sealed record PackingObjective(
    LinearExpr Expression, long Upper, long Current, string Label, bool Fixed = false)
{
    // 低于 double 的精确整数极限，为求解器内部界限运算保留余量。
    private const long SafeMaximum = (1L << 50) - 1;

    /// <summary>权重只由本网格决定；固定项可省常数，不能省它对前层的倍率。</summary>
    public static IReadOnlyList<PackingObjective> Sum(CpModel model,
        IReadOnlyList<IReadOnlyList<PackingObjective>> grids)
    {
        var terms = new List<(PackingObjective Goal, BigInteger Weight)>();
        foreach (IReadOnlyList<PackingObjective> grid in grids)
        {
            BigInteger weight = BigInteger.One;
            foreach (PackingObjective goal in grid.Reverse())
            {
                if (!goal.Fixed && goal.Upper > 0) { terms.Add((goal, weight)); }
                weight *= goal.Upper + 1;
            }
        }
        if (terms.Count == 0) { return new PackingObjective[0]; }
        BigInteger upper = terms.Aggregate(BigInteger.Zero,
            (sum, term) => sum + term.Weight * term.Goal.Upper);
        if (upper <= SafeMaximum)
        {
            return new[] { new PackingObjective(LinearExpr.Sum(terms.Select(t =>
                t.Goal.Expression * (long)t.Weight)), (long)upper,
                (long)terms.Aggregate(BigInteger.Zero,
                    (sum, t) => sum + t.Weight * t.Goal.Current), "grid-sum") };
        }

        // 拆的是最终总分的数位，进位包括所有网格；不按类别层级重新分组。
        long radix = System.Math.Min(1L << 20,
            SafeMaximum / (terms.Sum(t => t.Goal.Upper) + 1));
        var digits = new List<PackingObjective>();
        LinearExpr carry = LinearExpr.Constant(0);
        long carryUpper = 0;
        long carryCurrent = 0;
        while (terms.Any(t => t.Weight > 0) || carryUpper > 0)
        {
            long columnUpper = carryUpper;
            long columnCurrent = carryCurrent;
            var column = new List<LinearExpr> { carry };
            for (int i = 0; i < terms.Count; i++)
            {
                (PackingObjective goal, BigInteger weight) = terms[i];
                long coefficient = (long)(weight % radix);
                column.Add(goal.Expression * coefficient);
                columnUpper += goal.Upper * coefficient;
                columnCurrent += goal.Current * coefficient;
                terms[i] = (goal, weight / radix);
            }
            string label = "digit-" + digits.Count;
            long digitUpper = System.Math.Min(radix - 1, columnUpper);
            IntVar digit = model.NewIntVar(0, digitUpper, label);
            IntVar nextCarry = model.NewIntVar(0, columnUpper / radix,
                label + "-carry");
            model.Add(LinearExpr.Sum(column) == digit + nextCarry * radix);
            model.AddHint(digit, columnCurrent % radix);
            model.AddHint(nextCarry, columnCurrent / radix);
            digits.Add(new PackingObjective(digit, digitUpper,
                columnCurrent % radix, label));
            carry = nextCarry;
            carryUpper = columnUpper / radix;
            carryCurrent = columnCurrent / radix;
        }
        digits.Reverse();
        return Combine(digits);
    }

    public static IReadOnlyList<PackingObjective> Combine(
        IReadOnlyList<PackingObjective> objectives)
    {
        var combined = new List<PackingObjective>();
        foreach (PackingObjective next in objectives)
        {
            if (combined.Count == 0 || next.Upper >= SafeMaximum
                || combined.Last().Upper
                    > (SafeMaximum - next.Upper) / (next.Upper + 1))
            {
                combined.Add(next);
                continue;
            }
            PackingObjective before = combined.Last();
            long weight = next.Upper + 1;
            combined[combined.Count - 1] = new PackingObjective(
                before.Expression * weight + next.Expression,
                before.Upper * weight + next.Upper,
                before.Current * weight + next.Current,
                before.Label + "," + next.Label);
        }
        return combined;
    }
}
