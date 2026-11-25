using DentalLab.Domain.Entities;
using DentalLab.Domain.Services;
using Xunit;

namespace DentalLab.Domain.Tests;

public class CaseTests
{
    private static readonly DateTime Now = new(2026, 5, 10, 8, 0, 0, DateTimeKind.Utc);

    private static LabCase NewCase(int qty = 2, decimal price = 100m, decimal discount = 0m, int[]? teeth = null)
        => LabCase.Create("C-2026-000001", 1, 1, 1, null, "A2", qty, price, discount, Now.AddDays(7), null,
            teeth ?? [11, 12], Now);

    [Theory]
    [InlineData(2, 100, 0, 200)]
    [InlineData(3, 33.33, 0.99, 99.00)]
    [InlineData(1, 50, 50, 0)]
    public void Total_IsQuantityTimesPriceMinusDiscount(int q, double p, double d, double expected)
        => Assert.Equal((decimal)expected, LabCase.CalculateTotal(q, (decimal)p, (decimal)d));

    [Fact]
    public void Create_StartsReceived_WithHistoryAndTeeth()
    {
        var c = NewCase(teeth: [12, 11, 12]);
        Assert.Equal(CaseStatus.Received, c.Status);
        Assert.Equal(200m, c.Total);
        Assert.Equal(new[] { 11, 12 }, c.Teeth.Select(t => t.ToothNumber).ToArray());
        var h = Assert.Single(c.History);
        Assert.Null(h.FromStatus);
        Assert.Equal(CaseStatus.Received, h.ToStatus);
    }

    [Fact]
    public void Create_DiscountAboveSubtotal_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => NewCase(1, 100m, 100.01m));

    [Fact]
    public void Create_InvalidTooth_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => NewCase(teeth: [11, 99]));

    [Fact]
    public void Create_ZeroQuantity_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => NewCase(0));

    [Fact]
    public void FullWorkflow_RecordsEveryTransition_AndSetsDeliveryDate()
    {
        var c = NewCase();
        var t = Now;
        foreach (var s in new[] { CaseStatus.Designing, CaseStatus.Production, CaseStatus.Finishing,
                                  CaseStatus.QualityControl, CaseStatus.Ready, CaseStatus.Delivered })
            c.ChangeStatus(s, null, t = t.AddHours(1));

        Assert.Equal(CaseStatus.Delivered, c.Status);
        Assert.Equal(t, c.ActualDeliveryUtc);
        Assert.Equal(7, c.History.Count);
    }

    [Theory]
    [InlineData(CaseStatus.Received, CaseStatus.Ready)]
    [InlineData(CaseStatus.Received, CaseStatus.Delivered)]
    [InlineData(CaseStatus.Production, CaseStatus.Designing)]
    [InlineData(CaseStatus.Delivered, CaseStatus.Production)]
    [InlineData(CaseStatus.Cancelled, CaseStatus.Received)]
    public void InvalidTransitions_AreBlocked(CaseStatus from, CaseStatus to)
        => Assert.False(CaseWorkflow.CanTransition(from, to));

    [Fact]
    public void ChangeStatus_Invalid_ThrowsAndLeavesStateUntouched()
    {
        var c = NewCase();
        Assert.Throws<InvalidOperationException>(() => c.ChangeStatus(CaseStatus.Delivered, null, Now));
        Assert.Equal(CaseStatus.Received, c.Status);
        Assert.Single(c.History);
    }

    [Fact]
    public void QualityControl_CanGoBackToProduction_ForRework()
        => Assert.True(CaseWorkflow.CanTransition(CaseStatus.QualityControl, CaseStatus.Production));

    [Fact]
    public void FinalStatuses_HaveNoNextSteps()
    {
        Assert.Empty(CaseWorkflow.AllowedNext(CaseStatus.Delivered));
        Assert.Empty(CaseWorkflow.AllowedNext(CaseStatus.Cancelled));
    }

    [Fact]
    public void Update_Recalculates_ButFinalCasesAreLocked()
    {
        var c = NewCase();
        c.UpdateDetails(1, 1, 1, null, null, 3, 100m, 10m, Now.AddDays(5), null, [11]);
        Assert.Equal(290m, c.Total);

        c.ChangeStatus(CaseStatus.Cancelled, null, Now);
        Assert.Throws<InvalidOperationException>(() =>
            c.UpdateDetails(1, 1, 1, null, null, 1, 1m, 0m, Now, null, []));
    }

    [Fact]
    public void IsOverdue_OnlyWhilePastDueAndNotFinal()
    {
        var c = NewCase(); // expected = Now + 7d
        Assert.False(c.IsOverdue(Now));
        Assert.True(c.IsOverdue(Now.AddDays(8)));
        c.ChangeStatus(CaseStatus.Cancelled, null, Now);
        Assert.False(c.IsOverdue(Now.AddDays(8)));
    }

    [Fact]
    public void CaseNumber_FormatsAndParses()
    {
        Assert.Equal("C-2026-000042", CaseNumber.Format(2026, 42));
        Assert.Equal(42, CaseNumber.ParseSequence("C-2026-000042"));
        Assert.Equal("C-2026-", CaseNumber.Prefix(2026));
        Assert.Throws<FormatException>(() => CaseNumber.ParseSequence("garbage"));
    }
}
