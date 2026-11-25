using DentalLab.Application.Billing;
using DentalLab.Application.Doctors;

namespace DentalLab.Application.Abstractions;

/// <summary>
/// Invoices, payments and manual adjustments. Every operation that touches several records
/// (invoice + ledger + cases, payment + ledger + invoice) runs in one database transaction.
/// Validation problems are thrown as FluentValidation.ValidationException with error codes (Val_*).
/// </summary>
public interface IBillingService
{
    Task<IReadOnlyList<UnbilledCaseItem>> GetUnbilledCasesAsync(int doctorId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
    Task<int> CreateInvoiceAsync(InvoiceInput input, CancellationToken ct = default);
    Task VoidInvoiceAsync(int invoiceId, string? reason, CancellationToken ct = default);
    Task<PagedResult<InvoiceListItem>> SearchInvoicesAsync(InvoiceQuery query, CancellationToken ct = default);
    Task<InvoiceDetails?> GetInvoiceAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<OpenInvoiceItem>> GetOpenInvoicesAsync(int doctorId, CancellationToken ct = default);

    Task<int> RecordPaymentAsync(PaymentInput input, CancellationToken ct = default);
    Task ReversePaymentAsync(int paymentId, string? reason, CancellationToken ct = default);
    Task<PaymentListItem?> GetPaymentAsync(int paymentId, CancellationToken ct = default);
    Task<PagedResult<PaymentListItem>> SearchPaymentsAsync(PaymentQuery query, CancellationToken ct = default);

    Task AddAdjustmentAsync(AdjustmentInput input, CancellationToken ct = default);
}
