using DentalLab.Domain.Entities;

namespace DentalLab.Domain.Services;

public sealed record StatementLine(
    DateTime DateUtc, LedgerEntryType Type, string? Description,
    decimal Debit, decimal Credit, decimal RunningBalance);

/// <summary>The one place balances are computed. Balance = sum of signed ledger amounts.</summary>
public static class LedgerCalculator
{
    public static decimal Balance(IEnumerable<decimal> signedAmounts)
        => Money.Round(signedAmounts.Sum());

    public static IReadOnlyList<StatementLine> BuildStatement(IEnumerable<LedgerEntry> entries)
    {
        var lines = new List<StatementLine>();
        decimal running = 0;
        foreach (var e in entries.OrderBy(x => x.DateUtc).ThenBy(x => x.Id))
        {
            running = Money.Round(running + e.Amount);
            lines.Add(new StatementLine(e.DateUtc, e.Type, e.Description,
                e.Amount > 0 ? e.Amount : 0, e.Amount < 0 ? -e.Amount : 0, running));
        }
        return lines;
    }
}
