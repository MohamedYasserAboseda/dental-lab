namespace DentalLab.Domain.Entities;

public enum LedgerEntryType
{
    OpeningBalance = 1,
    Charge = 2,
    Payment = 3,
    Adjustment = 4,
    Reversal = 5
}

/// <summary>
/// One immutable line of a doctor's account. Amount is SIGNED:
/// positive = the doctor owes the lab (debit), negative = credit (payment / discount).
/// Entries are never edited or deleted: mistakes are fixed with a Reversal or Adjustment.
/// </summary>
public class LedgerEntry
{
    public long Id { get; private set; }
    public int DoctorId { get; private set; }
    public Doctor? Doctor { get; private set; }
    public DateTime DateUtc { get; private set; }
    public LedgerEntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string? Description { get; private set; }
    public long? ReversesEntryId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public int? InvoiceId { get; private set; }
    public int? PaymentId { get; private set; }

    private LedgerEntry() { }

    /// <summary>Records which invoice/payment produced this entry (traceability).</summary>
    public LedgerEntry LinkTo(int? invoiceId = null, int? paymentId = null)
    {
        InvoiceId = invoiceId; PaymentId = paymentId;
        return this;
    }

    public static LedgerEntry Opening(decimal signedAmount, DateTime utcNow)
        => Build(0, LedgerEntryType.OpeningBalance, Money.Round(signedAmount), null, utcNow, null);

    public static LedgerEntry Charge(int doctorId, decimal amount, DateTime utcNow, string? description = null)
    {
        amount = Money.Round(amount);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Charge must be positive.");
        return Build(doctorId, LedgerEntryType.Charge, amount, description, utcNow, null);
    }

    public static LedgerEntry Payment(int doctorId, decimal amount, DateTime utcNow, string? description = null)
    {
        amount = Money.Round(amount);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Payment must be positive.");
        return Build(doctorId, LedgerEntryType.Payment, -amount, description, utcNow, null);
    }

    public static LedgerEntry Adjustment(int doctorId, decimal signedAmount, string description, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("An adjustment needs a description.", nameof(description));
        return Build(doctorId, LedgerEntryType.Adjustment, Money.Round(signedAmount), description, utcNow, null);
    }

    public static LedgerEntry Reversal(LedgerEntry original, DateTime utcNow, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Type == LedgerEntryType.Reversal)
            throw new InvalidOperationException("A reversal cannot be reversed.");
        if (original.Id <= 0)
            throw new InvalidOperationException("Only saved entries can be reversed.");
        return Build(original.DoctorId, LedgerEntryType.Reversal, -original.Amount, reason, utcNow, original.Id);
    }

    private static LedgerEntry Build(int doctorId, LedgerEntryType type, decimal amount, string? description,
        DateTime utcNow, long? reverses)
    {
        if (amount == 0 && type != LedgerEntryType.OpeningBalance)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be zero.");
        return new LedgerEntry
        {
            DoctorId = doctorId,
            Type = type,
            Amount = amount,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            DateUtc = utcNow,
            CreatedUtc = utcNow,
            ReversesEntryId = reverses
        };
    }
}
