using DentalLab.Application.Abstractions;
using DentalLab.Application.Documents;
using DentalLab.Shared;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace DentalLab.Infrastructure.Documents;

public sealed record DocumentFolders(string Invoices, string Receipts)
{
    public static DocumentFolders Default => new(AppPaths.Invoices, AppPaths.Receipts);
}

public sealed class PdfDocumentService(
    IBillingService billing, IDoctorService doctors, ICaseService cases, ILabSettingsService labs,
    DocumentFolders folders, ILogger<PdfDocumentService>? log = null) : IDocumentService
{
    static PdfDocumentService() => QuestPDF.Settings.License = LicenseType.Community; // free for businesses under the QuestPDF revenue limit

    public async Task<PrintableDocument> CreateInvoiceAsync(int invoiceId, DocumentRequest request, CancellationToken ct = default)
    {
        var inv = await billing.GetInvoiceAsync(invoiceId, ct) ?? throw new InvalidOperationException($"Invoice {invoiceId} not found.");
        var doctor = await doctors.GetAsync(inv.DoctorId, ct);
        var c = await ContextAsync(request, ct);

        var lines = new List<InvoiceLineView>();
        foreach (var l in inv.Lines)
        {
            var cs = await cases.GetAsync(l.CaseId, ct);
            var text = l.CaseNumber;
            if (cs is not null)
            {
                var restoration = c.Rtl && !string.IsNullOrWhiteSpace(cs.RestorationNameAr) ? cs.RestorationNameAr : cs.RestorationName;
                text = $"{l.CaseNumber} — {cs.PatientName} — {restoration}" + (cs.Teeth.Count > 0 ? $" ({string.Join(", ", cs.Teeth)})" : "");
            }
            lines.Add(new InvoiceLineView(text, l.Quantity, l.UnitPrice, l.Discount, l.Amount));
        }

        var doc = InvoicePdf.Build(inv, doctor, lines, c);
        return await RenderAsync(doc, $"{inv.Number}.pdf", folders.Invoices, ct);
    }

    public async Task<PrintableDocument> CreateReceiptAsync(int paymentId, DocumentRequest request, CancellationToken ct = default)
    {
        var p = await billing.GetPaymentAsync(paymentId, ct) ?? throw new InvalidOperationException($"Payment {paymentId} not found.");
        var doc = ReceiptPdf.Build(p, await ContextAsync(request, ct));
        return await RenderAsync(doc, $"{p.ReceiptNumber}.pdf", folders.Receipts, ct);
    }

    public async Task<PrintableDocument> CreateStatementAsync(int doctorId, DocumentRequest request, CancellationToken ct = default)
    {
        var d = await doctors.GetAsync(doctorId, ct) ?? throw new InvalidOperationException($"Doctor {doctorId} not found.");
        var lines = await doctors.GetStatementAsync(doctorId, ct);
        var doc = StatementPdf.Build(d, lines, await ContextAsync(request, ct));
        return await RenderAsync(doc, $"statement-{d.Code}-{DateTime.Today:yyyyMMdd}.pdf", null, ct);
    }

    public async Task<PrintableDocument> CreateWorkOrderAsync(int caseId, DocumentRequest request, CancellationToken ct = default)
    {
        var cs = await cases.GetAsync(caseId, ct) ?? throw new InvalidOperationException($"Case {caseId} not found.");
        var doc = WorkOrderPdf.Build(cs, await ContextAsync(request, ct));
        return await RenderAsync(doc, $"workorder-{cs.CaseNumber}.pdf", null, ct);
    }

    private async Task<DocContext> ContextAsync(DocumentRequest r, CancellationToken ct)
    {
        var settings = await labs.GetAsync(ct);
        byte[]? logo = null;
        var path = settings.Profile.LogoPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { logo = await File.ReadAllBytesAsync(path, ct); }
            catch (IOException ex) { log?.LogWarning(ex, "The logo could not be read"); }
        }
        return new DocContext { Format = r.Format, Language = r.Language, Lab = settings.Profile, Logo = logo, T = new DocText(r.Language) };
    }

    private async Task<PrintableDocument> RenderAsync(Document doc, string fileName, string? archiveFolder, CancellationToken ct)
    {
        var (pdf, pages) = await Task.Run(() =>
        {
            var bytes = doc.GeneratePdf();
            var images = doc.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 200 }).ToList();
            return (bytes, (IReadOnlyList<byte[]>)images);
        }, ct);

        if (archiveFolder is not null)
        {
            try
            {
                Directory.CreateDirectory(archiveFolder);
                await File.WriteAllBytesAsync(Path.Combine(archiveFolder, fileName), pdf, ct); // best effort: the printout never fails because of this
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log?.LogWarning(ex, "Archiving {File} failed", fileName); }
        }
        return new PrintableDocument(fileName, pdf, pages);
    }
}
