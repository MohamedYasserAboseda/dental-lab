using DentalLab.Application.Abstractions;
using DentalLab.Application.Billing;
using DentalLab.Application.Doctors;
using DentalLab.Domain.Entities;
using DentalLab.Domain.Services;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace DentalLab.Infrastructure.Persistence;

public sealed class BillingService(
    IDbContextFactory<DentalLabDbContext> factory,
    IValidator<InvoiceInput> invoiceValidator,
    IValidator<PaymentInput> paymentValidator,
    IValidator<AdjustmentInput> adjustmentValidator) : IBillingService
{
    // ---------------------------------------------------------------- invoices

    public async Task<IReadOnlyList<UnbilledCaseItem>> GetUnbilledCasesAsync(int doctorId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Cases.AsNoTracking().Where(c => c.DoctorId == doctorId && c.InvoiceId == null && c.Status != CaseStatus.Cancelled);
        if (fromUtc is not null) q = q.Where(c => c.ReceivedUtc >= fromUtc);
        if (toUtc is not null) q = q.Where(c => c.ReceivedUtc < toUtc);

        return await q.OrderBy(c => c.ReceivedUtc).ThenBy(c => c.Id)
            .Select(c => new UnbilledCaseItem(c.Id, c.CaseNumber, c.Patient!.FullName, c.RestorationType!.Name,
                c.RestorationType.NameAr, c.ReceivedUtc, c.Status, c.Quantity, c.Total))
            .ToListAsync(ct);
    }

    public async Task<int> CreateInvoiceAsync(InvoiceInput input, CancellationToken ct = default)
    {
        await invoiceValidator.ValidateAndThrowAsync(input, ct);
        var ids = input.CaseIds.Distinct().ToList();

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!await db.Doctors.AnyAsync(d => d.Id == input.DoctorId, ct)) throw Fail("DoctorId", "DoctorRequired");

        var cases = await db.Cases.Where(c => ids.Contains(c.Id) && c.DoctorId == input.DoctorId).ToListAsync(ct);
        if (cases.Count != ids.Count) throw Fail("CaseIds", "CaseNotFound");
        if (cases.Any(c => c.IsInvoiced)) throw Fail("CaseIds", "AlreadyInvoiced");
        if (cases.Any(c => c.Status == CaseStatus.Cancelled)) throw Fail("CaseIds", "CaseCancelled");

        var subtotal = Domain.Money.Round(cases.Sum(c => c.Total));
        var discount = Domain.Money.Round(input.Discount);
        if (discount > subtotal) throw Fail("Discount", "DiscountInvalid");
        if (subtotal - discount <= 0) throw Fail("Discount", "InvoiceTotalInvalid");

        var now = DateTime.UtcNow;
        var number = await NextNumberAsync(db, DocumentNumber.Invoice,
            p => db.Invoices.Where(i => i.InvoiceNumber.StartsWith(p)).OrderByDescending(i => i.InvoiceNumber).Select(i => i.InvoiceNumber), ct);

        var invoice = Invoice.Create(number, input.DoctorId, cases, discount, input.Description, now);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct); // need the invoice id for the links below

        foreach (var c in cases) c.AssignInvoice(invoice.Id);
        db.LedgerEntries.Add(LedgerEntry.Charge(input.DoctorId, invoice.Total, now, invoice.InvoiceNumber).LinkTo(invoiceId: invoice.Id));
        db.AuditLogs.Add(AuditLog.Create("InvoiceCreated", nameof(Invoice), invoice.Id.ToString(), $"{number} total={invoice.Total}"));
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return invoice.Id;
    }

    public async Task VoidInvoiceAsync(int invoiceId, string? reason, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found.");
        if (invoice.Status == InvoiceStatus.Voided) throw Fail("Invoice", "AlreadyVoided");
        if (invoice.PaidAmount > 0) throw Fail("Invoice", "InvoiceHasPayments");

        var now = DateTime.UtcNow;
        var charge = await db.LedgerEntries.FirstOrDefaultAsync(l => l.InvoiceId == invoiceId && l.Type == LedgerEntryType.Charge, ct)
            ?? throw new InvalidOperationException("The invoice charge is missing from the ledger.");

        db.LedgerEntries.Add(LedgerEntry.Reversal(charge, now, reason).LinkTo(invoiceId: invoiceId));
        foreach (var c in await db.Cases.Where(c => c.InvoiceId == invoiceId).ToListAsync(ct)) c.ReleaseInvoice();
        invoice.Void(now, reason);
        db.AuditLogs.Add(AuditLog.Create("InvoiceVoided", nameof(Invoice), invoiceId.ToString(), invoice.InvoiceNumber));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<PagedResult<InvoiceListItem>> SearchInvoicesAsync(InvoiceQuery q, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<Invoice> query = db.Invoices.AsNoTracking();
        if (q.DoctorId is > 0) query = query.Where(i => i.DoctorId == q.DoctorId);
        if (q.Status is not null) query = query.Where(i => i.Status == q.Status);

        var term = q.Term?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var like = $"%{EscapeLike(term)}%";
            query = query.Where(i => EF.Functions.Like(i.InvoiceNumber, like, "\\") || EF.Functions.Like(i.Doctor!.FullName, like, "\\"));
        }

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var rows = await query.OrderByDescending(i => i.DateUtc).ThenByDescending(i => i.Id).Skip((page - 1) * size).Take(size)
            .Select(i => new { i.Id, i.InvoiceNumber, i.DoctorId, DoctorName = i.Doctor!.FullName, i.DateUtc, i.Total, i.PaidAmount, i.Status })
            .ToListAsync(ct);

        var items = rows.Select(r => new InvoiceListItem(r.Id, r.InvoiceNumber, r.DoctorId, r.DoctorName, r.DateUtc, r.Total, r.PaidAmount,
            r.Status == InvoiceStatus.Voided ? 0 : Domain.Money.Round(r.Total - r.PaidAmount), r.Status)).ToList();
        return new PagedResult<InvoiceListItem>(items, total);
    }

    public async Task<InvoiceDetails?> GetInvoiceAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var i = await db.Invoices.AsNoTracking().Include(x => x.Lines).Include(x => x.Doctor).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return null;

        var payments = await db.Payments.AsNoTracking().Where(p => p.InvoiceId == id).OrderBy(p => p.CreatedUtc).ToListAsync(ct);
        return new InvoiceDetails(i.Id, i.InvoiceNumber, i.DoctorId, i.Doctor!.FullName, i.DateUtc, i.Description, i.Subtotal, i.Discount,
            i.Total, i.PaidAmount, i.Remaining, i.Status, i.VoidedUtc, i.VoidReason,
            i.Lines.OrderBy(l => l.Id).Select(l => new InvoiceLineItem(l.CaseId, l.Description, l.Quantity, l.UnitPrice, l.Discount, l.Amount)).ToList(),
            payments.Select(p => new InvoicePaymentItem(p.Id, p.ReceiptNumber, p.Date, p.Amount, p.Method, p.IsReversed)).ToList());
    }

    public async Task<IReadOnlyList<OpenInvoiceItem>> GetOpenInvoicesAsync(int doctorId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Invoices.AsNoTracking()
            .Where(i => i.DoctorId == doctorId && (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.PartiallyPaid))
            .OrderBy(i => i.DateUtc).ToListAsync(ct);
        return rows.Select(i => new OpenInvoiceItem(i.Id, i.InvoiceNumber, i.Remaining)).ToList();
    }

    // ---------------------------------------------------------------- payments

    public async Task<int> RecordPaymentAsync(PaymentInput input, CancellationToken ct = default)
    {
        await paymentValidator.ValidateAndThrowAsync(input, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!await db.Doctors.AnyAsync(d => d.Id == input.DoctorId, ct)) throw Fail("DoctorId", "DoctorRequired");

        var amount = Domain.Money.Round(input.Amount);
        Invoice? invoice = null;
        if (input.InvoiceId is int invoiceId)
        {
            invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct) ?? throw Fail("InvoiceId", "InvoiceNotFound");
            if (invoice.DoctorId != input.DoctorId) throw Fail("InvoiceId", "InvoiceMismatch");
            if (invoice.Status == InvoiceStatus.Voided) throw Fail("InvoiceId", "InvoiceVoided");
            if (amount > invoice.Remaining) throw Fail("Amount", "Overpayment");
        }

        // Same doctor, amount, method and invoice within a minute = almost certainly a double click / double entry.
        var now = DateTime.UtcNow;
        var since = now.AddSeconds(-60);
        var recent = await db.Payments.AsNoTracking()
            .Where(p => p.DoctorId == input.DoctorId && p.InvoiceId == input.InvoiceId && p.Method == input.Method &&
                        !p.IsReversed && p.CreatedUtc >= since)
            .Select(p => p.Amount).ToListAsync(ct);
        if (recent.Any(a => a == amount)) throw Fail("Amount", "DuplicatePayment");

        var receipt = await NextNumberAsync(db, DocumentNumber.Receipt,
            p => db.Payments.Where(x => x.ReceiptNumber.StartsWith(p)).OrderByDescending(x => x.ReceiptNumber).Select(x => x.ReceiptNumber), ct);

        var payment = Payment.Create(receipt, input.DoctorId, input.InvoiceId, input.Date, amount, input.Method, input.Notes, now);
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        invoice?.ApplyPayment(amount);
        db.LedgerEntries.Add(LedgerEntry.Payment(input.DoctorId, amount, now, receipt).LinkTo(invoiceId: input.InvoiceId, paymentId: payment.Id));
        db.AuditLogs.Add(AuditLog.Create("PaymentRecorded", nameof(Payment), payment.Id.ToString(), $"{receipt} amount={amount}"));
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return payment.Id;
    }

    public async Task ReversePaymentAsync(int paymentId, string? reason, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw new InvalidOperationException($"Payment {paymentId} not found.");
        if (payment.IsReversed) throw Fail("Payment", "AlreadyReversed");

        var now = DateTime.UtcNow;
        var original = await db.LedgerEntries.FirstOrDefaultAsync(l => l.PaymentId == paymentId && l.Type == LedgerEntryType.Payment, ct)
            ?? throw new InvalidOperationException("The payment is missing from the ledger.");

        db.LedgerEntries.Add(LedgerEntry.Reversal(original, now, reason ?? payment.ReceiptNumber).LinkTo(invoiceId: payment.InvoiceId, paymentId: paymentId));
        if (payment.InvoiceId is int invoiceId)
        {
            var invoice = await db.Invoices.FirstAsync(i => i.Id == invoiceId, ct);
            invoice.ReversePayment(payment.Amount);
        }
        payment.Reverse(now);
        db.AuditLogs.Add(AuditLog.Create("PaymentReversed", nameof(Payment), paymentId.ToString(), payment.ReceiptNumber));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<PaymentListItem?> GetPaymentAsync(int paymentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var r = await db.Payments.AsNoTracking().Where(p => p.Id == paymentId)
            .Select(p => new
            {
                p.Id, p.ReceiptNumber, p.DoctorId, DoctorName = p.Doctor!.FullName, p.Date, p.Amount, p.Method, p.IsReversed, p.Notes,
                InvoiceNumber = db.Invoices.Where(i => i.Id == p.InvoiceId).Select(i => i.InvoiceNumber).FirstOrDefault()
            }).FirstOrDefaultAsync(ct);
        return r is null ? null : new PaymentListItem(r.Id, r.ReceiptNumber, r.DoctorId, r.DoctorName, r.Date, r.Amount, r.Method,
            r.InvoiceNumber, r.IsReversed, r.Notes);
    }

    public async Task<PagedResult<PaymentListItem>> SearchPaymentsAsync(PaymentQuery q, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<Payment> query = db.Payments.AsNoTracking();
        if (q.DoctorId is > 0) query = query.Where(p => p.DoctorId == q.DoctorId);

        var term = q.Term?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var like = $"%{EscapeLike(term)}%";
            query = query.Where(p => EF.Functions.Like(p.ReceiptNumber, like, "\\") || EF.Functions.Like(p.Doctor!.FullName, like, "\\"));
        }

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var rows = await query.OrderByDescending(p => p.CreatedUtc).ThenByDescending(p => p.Id).Skip((page - 1) * size).Take(size)
            .Select(p => new
            {
                p.Id, p.ReceiptNumber, p.DoctorId, DoctorName = p.Doctor!.FullName, p.Date, p.Amount, p.Method, p.IsReversed, p.Notes,
                InvoiceNumber = db.Invoices.Where(i => i.Id == p.InvoiceId).Select(i => i.InvoiceNumber).FirstOrDefault()
            }).ToListAsync(ct);

        return new PagedResult<PaymentListItem>(rows.Select(r => new PaymentListItem(r.Id, r.ReceiptNumber, r.DoctorId, r.DoctorName,
            r.Date, r.Amount, r.Method, r.InvoiceNumber, r.IsReversed, r.Notes)).ToList(), total);
    }

    // ---------------------------------------------------------------- adjustments

    public async Task AddAdjustmentAsync(AdjustmentInput input, CancellationToken ct = default)
    {
        await adjustmentValidator.ValidateAndThrowAsync(input, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Doctors.AnyAsync(d => d.Id == input.DoctorId, ct)) throw Fail("DoctorId", "DoctorRequired");

        var now = DateTime.UtcNow;
        db.LedgerEntries.Add(LedgerEntry.Adjustment(input.DoctorId, input.Amount, input.Description, now));
        db.AuditLogs.Add(AuditLog.Create("LedgerAdjustment", nameof(Doctor), input.DoctorId.ToString(), $"amount={Domain.Money.Round(input.Amount)}"));
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<string> NextNumberAsync(DentalLabDbContext db, string code,
        Func<string, IQueryable<string>> lastNumberQuery, CancellationToken ct)
    {
        var year = DateTime.Now.Year;
        var last = await lastNumberQuery(DocumentNumber.Prefix(code, year)).FirstOrDefaultAsync(ct);
        return DocumentNumber.Format(code, year, last is null ? 1 : DocumentNumber.ParseSequence(last) + 1);
    }

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static ValidationException Fail(string property, string code)
        => new([new ValidationFailure(property, code) { ErrorCode = code }]);
}
