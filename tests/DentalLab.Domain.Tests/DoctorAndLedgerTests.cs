using DentalLab.Domain;
using DentalLab.Domain.Entities;
using DentalLab.Domain.Services;
using Xunit;

namespace DentalLab.Domain.Tests;

public class DoctorAndLedgerTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Money_RoundsToTwoDecimals_AwayFromZero()
    {
        Assert.Equal(150.51m, Money.Round(150.505m));
        Assert.Equal(-150.51m, Money.Round(-150.505m));
    }

    [Fact]
    public void Create_WithOpeningBalance_AddsOneLedgerEntry()
    {
        var d = Doctor.Create("Dr Ahmed", null, null, null, null, null, 250.756m, true, Now);
        Assert.Equal(250.76m, d.OpeningBalance);
        var entry = Assert.Single(d.LedgerEntries);
        Assert.Equal(LedgerEntryType.OpeningBalance, entry.Type);
        Assert.Equal(250.76m, entry.Amount);
    }

    [Fact]
    public void Create_WithZeroOpeningBalance_AddsNoEntry()
    {
        var d = Doctor.Create("Dr Ahmed", null, null, null, null, null, 0m, true, Now);
        Assert.Empty(d.LedgerEntries);
    }

    [Fact]
    public void Create_TrimsAndNormalizesBlankFieldsToNull()
    {
        var d = Doctor.Create("  Dr Ahmed ", "  ", " 0100 ", "", null, "  ", 0, true, Now);
        Assert.Equal("Dr Ahmed", d.FullName);
        Assert.Null(d.ClinicName);
        Assert.Equal("0100", d.Phone);
        Assert.Null(d.Email);
        Assert.Null(d.Notes);
    }

    [Fact]
    public void Create_BlankName_Throws()
        => Assert.Throws<ArgumentException>(() => Doctor.Create(" ", null, null, null, null, null, 0, true));

    [Fact]
    public void Payment_IsStoredNegative_AndChargeIsPositive()
    {
        Assert.Equal(-100m, LedgerEntry.Payment(1, 100m, Now).Amount);
        Assert.Equal(100m, LedgerEntry.Charge(1, 100m, Now).Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Payment_NonPositive_Throws(double amount)
        => Assert.Throws<ArgumentOutOfRangeException>(() => LedgerEntry.Payment(1, (decimal)amount, Now));

    [Fact]
    public void Adjustment_WithoutDescription_Throws()
        => Assert.Throws<ArgumentException>(() => LedgerEntry.Adjustment(1, 10m, " ", Now));

    [Fact]
    public void Statement_RunningBalance_IsCumulative()
    {
        var entries = new[]
        {
            LedgerEntry.Opening(100m, Now),
            LedgerEntry.Charge(1, 50.25m, Now.AddDays(1)),
            LedgerEntry.Payment(1, 120m, Now.AddDays(2)),
        };
        var lines = LedgerCalculator.BuildStatement(entries);
        Assert.Equal(new[] { 100m, 150.25m, 30.25m }, lines.Select(l => l.RunningBalance).ToArray());
        Assert.Equal(120m, lines[2].Credit);
        Assert.Equal(0m, lines[2].Debit);
        Assert.Equal(30.25m, LedgerCalculator.Balance(entries.Select(e => e.Amount)));
    }

    [Fact]
    public void Balance_HasNoFloatingPointDrift()
    {
        var amounts = Enumerable.Repeat(0.1m, 10);
        Assert.Equal(1.00m, LedgerCalculator.Balance(amounts));
    }
}
