using DentalLab.Application.Documents;

namespace DentalLab.Application.Abstractions;

/// <summary>Builds the printable documents from the stored data. Invoices and receipts are also archived as PDF files.</summary>
public interface IDocumentService
{
    /// <summary>Used for both individual-case and consolidated invoices (the lines come from the invoice).</summary>
    Task<PrintableDocument> CreateInvoiceAsync(int invoiceId, DocumentRequest request, CancellationToken ct = default);
    Task<PrintableDocument> CreateReceiptAsync(int paymentId, DocumentRequest request, CancellationToken ct = default);
    Task<PrintableDocument> CreateStatementAsync(int doctorId, DocumentRequest request, CancellationToken ct = default);
    Task<PrintableDocument> CreateWorkOrderAsync(int caseId, DocumentRequest request, CancellationToken ct = default);
}
