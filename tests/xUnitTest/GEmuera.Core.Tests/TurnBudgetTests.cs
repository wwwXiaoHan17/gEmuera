using GEmuera.Core.Agent.Context;
using Xunit;

namespace GEmuera.Core.Tests;

public class TurnBudgetTests
{
    [Fact]
    public void InitialBudget_CanConsume()
    {
        var budget = new TurnBudget(8000);
        Assert.Equal(8000, budget.Remaining);
        Assert.True(budget.TryConsume(5000));
        Assert.Equal(3000, budget.Remaining);
    }

    [Fact]
    public void Exhausted_RefusesAndDoesNotOverdraw()
    {
        var budget = new TurnBudget(100);
        Assert.False(budget.TryConsume(200));
        Assert.Equal(100, budget.Remaining);
        Assert.True(budget.TryConsume(100));
        Assert.Equal(0, budget.Remaining);
        Assert.False(budget.TryConsume(1));
        Assert.Equal(0, budget.Remaining);
    }

    [Fact]
    public void Reset_RestoresFullBudget()
    {
        var budget = new TurnBudget(1000);
        Assert.True(budget.TryConsume(1000));
        Assert.Equal(0, budget.Remaining);
        budget.Reset();
        Assert.Equal(1000, budget.Remaining);
    }

    [Fact]
    public void NegativeConsume_Rejected()
    {
        var budget = new TurnBudget(100);
        Assert.False(budget.TryConsume(-1));
        Assert.Equal(100, budget.Remaining);
    }
}
