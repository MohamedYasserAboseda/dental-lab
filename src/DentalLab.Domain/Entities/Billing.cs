namespace DentalLab.Domain.Entities;

public enum InvoiceStatus { Unpaid = 1, PartiallyPaid = 2, Paid = 3, Voided = 4 }
public enum PaymentMethod { Cash = 1, Card = 2, BankTransfer = 3, Cheque = 4, Other = 5 }

/// <summary>Snapshot of one case at invoicing time.</summary>
public class InvoiceLine
{
    public int Id { get; private set; }
    public int InvoiceId { get; private set; }
    public int CaseId { get; private set; }
    public string Description { get; private set; } = string.Empty; // case number
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Amount { get; private set; }

    private InvoiceLine() { }
    public InvoiceLine(LabCase c)
    {
        CaseId = c.Id; Description = c.CaseNumber; Quantity = c.Quantity;
        UnitPrice = c.UnitPrice; Discount = c.Discount; Amount = c.Total;
    }
}

public class Invoice
{
    public int Id { get; private set; }
    public string InvoiceNumber { get; private set; } = string.Empty;
    public int DoctorId { get; private set; }
    public Doctor? Doctor { get; private set; }
    public DateTime DateUtc { get; private set; }
    public string? Description { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Total { get; private set; }
    /// <summary>Sum of non-reversed payments applied to this invoice (kept in step by ApplyPayment/ReversePayment).</summary>
    public decimal PaidAmount { get; private set; }
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Unpaid;
    public DateTime? VoidedUtc { get; private set; }
    public string? VoidReason { get; private set; }
    public List<InvoiceLine> Lines { get; } = new();

    private Invoice() { }

    public decimal Remaining => Status == InvoiceStatus.Voided ? 0 : Money.Round(Total - PaidAmount);

    public static Invoice Create(string number, int doctorId, IReadOnlyList<LabCase> cases, decimal invoiceDiscount,
        string? description, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(number)) throw new ArgumentException("Invoice number is required.", nameof(number));
        if (cases.Count == 0) throw new ArgumentException("An invoice needs at least one case.", nameof(cases));
        if (cases.Any(c => c.DoctorId != doctorId)) throw new InvalidOperationException("All cases must belong to the invoiced doctor.");
        if (cases.Any(c => c.IsInvoiced)) throw new InvalidOperationException("A case cannot be invoiced twice.");
        if (cases.Any(c => c.Status == CaseStatus.Cancelled)) throw new InvalidOperationException("A cancelled case cannot be invoiced.");

        var subtotal = Money.Round(cases.Sum(c => c.Total));
        var discount = Money.Round(invoiceDiscount);
        if (discount < 0 || discount > subtotal) throw new ArgumentOutOfRangeException(nameof(invoiceDiscount), "Discount must be between 0 and the subtotal.");
        var total = Money.Round(subtotal - discount);
        if (total <= 0) throw new ArgumentOutOfRangeException(nameof(invoiceDiscount), "Invoice total must be greater than zero.");

        var inv = new Invoice
        {
            InvoiceNumber = number, DoctorId = doctorId, DateUtc = utcNow,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Subtotal = subtotal, Discount = discount, Total = total
        };
        foreach (var c in cases) inv.Lines.Add(new InvoiceLine(c));
        return inv;
    }

    public void ApplyPayment(decimal amount)
    {
        if (Status == InvoiceStatus.Voided) throw new InvalidOperationException("A voided invoice cannot receive payments.");
        amount = Money.Round(amount);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount > Remaining) throw new InvalidOperationException("Payment exceeds the remaining balance.");
        PaidAmount = Money.Round(PaidAmount + amount);
        Status = PaidAmount >= Total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
    }

    public void ReversePayment(decimal amount)
    {
        amount = Money.Round(amount);
        if (amount <= 0 || amount > PaidAmount) throw new ArgumentOutOfRangeException(nameof(amount));
        PaidAmount = Money.Round(PaidAmount - amount);
        Status = PaidAmount == 0 ? InvoiceStatus.Unpaid : InvoiceStatus.PartiallyPaid;
    }

    public void Void(DateTime utcNow, string? reason)
    {
        if (Status == InvoiceStatus.Voided) throw new InvalidOperationException("Invoice is already voided.");
        if (PaidAmount > 0) throw new InvalidOperationException("Reverse the payments before voiding the invoice.");
        Status = InvoiceStatus.Voided;
        VoidedUtc = utcNow;
        VoidReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}

public class Payment
{
    public int Id { get; private set; }
    public string ReceiptNumber { get; private set; } = string.Empty;
    public int DoctorId { get; private set; }
    public Doctor? Doctor { get; private set; }
    public int? InvoiceId { get; private set; }
    public DateTime Date { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public bool IsReversed { get; private set; }
    public DateTime? ReversedUtc { get; private set; }

    private Payment() { }

    public static Payment Create(string receiptNumber, int doctorId, int? invoiceId, DateTime date, decimal amount,
        PaymentMethod method, string? notes, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(receiptNumber)) throw new ArgumentException("Receipt number is required.", nameof(receiptNumber));
        amount = Money.Round(amount);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Payment must be positive.");
        if (!Enum.IsDefined(method)) throw new ArgumentOutOfRangeException(nameof(method));
        return new Payment
        {
            ReceiptNumber = receiptNumber, DoctorId = doctorId, InvoiceId = invoiceId, Date = date.Date, Amount = amount,
            Method = method, Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), CreatedUtc = utcNow
        };
    }

    public void Reverse(DateTime utcNow)
    {
        if (IsReversed) throw new InvalidOperationException("Payment is already reversed.");
        IsReversed = true;
        ReversedUtc = utcNow;
    }
}
