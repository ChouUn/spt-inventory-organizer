using ChouUn.InventoryOrganizer.Core.Organizing;
using Xunit;

namespace ChouUn.InventoryOrganizer.Core.Tests.Organizing;

public sealed class PackBudgetTests
{
    [Fact]
    public void 超额开销和零预算保底不侵占最终排布预留()
    {
        var budget = new PackBudget();
        budget.Charge(true, 1.1);
        budget.Charge(true, 1.1);
        budget.Charge(true, 0.1);
        Assert.Equal(2, budget.CollectionSeconds);
        Assert.Equal(1, budget.Remaining(false));
    }

    [Fact]
    public void 收纳用满两秒后最终排布仍有一秒()
    {
        var budget = new PackBudget();
        budget.Charge(true, 1);
        budget.Charge(true, 1);
        Assert.Equal(0, budget.Available(true));
        Assert.Equal(1, budget.Available(false));
        budget.Charge(false, 1);
        Assert.Equal(0, budget.Available(false));
    }

    [Fact]
    public void 收纳余量结转且单次最多一秒()
    {
        var budget = new PackBudget();
        budget.Charge(true, 0.4);
        Assert.Equal(1, budget.Available(false));
        budget.Charge(false, 1);
        Assert.Equal(1, budget.Available(false));
        budget.Charge(false, 1);
        Assert.Equal(0.6, budget.Available(false), 6);
    }
}
