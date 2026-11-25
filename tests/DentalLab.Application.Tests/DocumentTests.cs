using System.Resources;
using System.Globalization;
using DentalLab.Application;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Billing;
using DentalLab.Application.Cases;
using DentalLab.Application.Catalog;
using DentalLab.Application.Documents;
using DentalLab.Application.Doctors;
using DentalLab.Application.Patients;
using DentalLab.Domain.Entities;
using DentalLab.Infrastructure.Documents;
using DentalLab.Infrastructure.Persistence;
using DentalLab.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLab.Application.Tests;

public sealed class LabSettingsServiceTests : IDisposable
{
    private sealed class MemorySettings : ISettingsService
    {
        private readonly Dictionary<string, string> _v = [];
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_v.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken ct = default) { _v[key] = value; return Task.CompletedTask; }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dentallab-lab-" + Guid.NewGuid().ToString("N"));
    private readonly LabSettingsService _svc;

    public LabSettingsServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _svc = new LabSettingsService(new MemorySettings(), Path.Combine(_dir, "Branding"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Write(string name, byte[] bytes) { var p = Path.Combine(_dir, name); File.WriteAllBytes(p, bytes); return p; }
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0];

    [Fact]
    public async Task Defaults_AreSensible_AndSettingsRoundTrip()
    {
        var d = await _svc.GetAsync();
        Assert.Equal(PrintFormat.A4, d.DefaultFormat);
        Assert.Equal(DocLanguageMode.FollowApp, d.LanguageMode);
        Assert.Equal("EGP", d.Profile.Currency);

        var saved = new LabSettings(new LabProfile(" Smile Lab ", "Cairo", "0100", "a@b.com", null, "USD"), PrintFormat.Thermal80, DocLanguageMode.Arabic);
        await _svc.SaveAsync(saved);
        var back = await _svc.GetAsync();
        Assert.Equal("Smile Lab", back.Profile.Name);
        Assert.Equal("USD", back.Profile.Currency);
        Assert.Equal(PrintFormat.Thermal80, back.DefaultFormat);
        Assert.Equal(DocLanguageMode.Arabic, back.LanguageMode);
        Assert.Null(back.Profile.LogoPath);
    }

    [Fact]
    public async Task Logo_IsValidatedBySignature_CopiedAndReplaced()
    {
        var first = await _svc.SaveLogoAsync(Write("a.png", Png));
        Assert.EndsWith("logo.png", first);
        Assert.True(File.Exists(first));

        var second = await _svc.SaveLogoAsync(Write("renamed.txt", Jpg));   // the name does not matter, the content does
        Assert.EndsWith("logo.jpg", second);
        Assert.False(File.Exists(first));                                   // the old logo is replaced
    }

    [Fact]
    public async Task Logo_RejectsNonImages_EmptyFiles_AndOversizedFiles()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => _svc.SaveLogoAsync(Write("x.png", "<script>alert(1)</script>"u8.ToArray())));
        await Assert.ThrowsAsync<InvalidDataException>(() => _svc.SaveLogoAsync(Write("empty.png", [])));
        await Assert.ThrowsAsync<InvalidDataException>(() => _svc.SaveLogoAsync(Write("big.png", Png.Concat(new byte[LabSettingsService.MaxLogoBytes]).ToArray())));
        await Assert.ThrowsAsync<InvalidDataException>(() => _svc.SaveLogoAsync(Path.Combine(_dir, "missing.png")));
    }
}

public sealed class DocumentTextTests
{
    [Fact]
    public void EveryDocumentKey_ExistsInBothLanguages()
    {
        var rm = new ResourceManager("DentalLab.Shared.Resources.Strings", typeof(AppPaths).Assembly);
        var keys = new[]
        {
            "Doc_DefaultLabName", "Doc_Invoice", "Doc_ReceiptTitle", "Doc_Statement", "Doc_WorkOrder", "Doc_BillTo", "Doc_Item", "Doc_Voided",
            "Doc_Thanks", "Doc_ReceivedBy", "Doc_DoctorSignature", "Doc_Technician", "Doc_QC", "Doc_AmountReceived", "Doc_ClosingBalance",
            "Doc_PrintedOn", "Doc_RelatedInvoice", "Doc_OnAccount", "Doc_AppliedPayments", "Doc_AsOf",
            "Field_Quantity", "Field_UnitPrice", "Field_Discount", "Col_Amount", "Col_Paid", "Col_Remaining", "Details_Subtotal", "Field_Total",
            "Col_Date", "Col_Type", "Col_Description", "Col_Debit", "Col_Credit", "Col_RunningBalance", "Field_Doctor", "Field_Patient",
            "Field_Restoration", "Field_Material", "Field_Shade", "Field_Method", "Field_Notes", "Details_Received", "Col_Expected", "Col_Teeth",
            "Invoice_Reversed", "Profile_BalanceHint"
        }.Concat(Enum.GetNames<PaymentMethod>().Select(m => "Method_" + m))
         .Concat(Enum.GetNames<LedgerEntryType>().Select(t => "Ledger_" + t)).ToList();

        foreach (var lang in new[] { "en", "ar" })
            foreach (var key in keys)
                Assert.False(string.IsNullOrWhiteSpace(rm.GetString(key, new CultureInfo(lang))), $"{key} ({lang})");

        // Arabic really is translated (not just the English fallback)
        Assert.NotEqual(rm.GetString("Doc_Invoice", new CultureInfo("en")), rm.GetString("Doc_Invoice", new CultureInfo("ar")));
    }
}

/// <summary>Generates every document in every format and language (real QuestPDF rendering) from real data.</summary>
public sealed class PdfDocumentServiceTests : IDisposable
{
    private sealed class Factory(DbContextOptions<DentalLabDbContext> o) : IDbContextFactory<DentalLabDbContext>
    {
        public DentalLabDbContext CreateDbContext() => new(o);
    }
    private sealed class FixedLabs(LabSettings s) : ILabSettingsService
    {
        public Task<LabSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(s);
        public Task SaveAsync(LabSettings settings, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> SaveLogoAsync(string sourcePath, CancellationToken ct = default) => Task.FromResult(sourcePath);
    }

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dentallab-docs-" + Guid.NewGuid().ToString("N"));
    private readonly DoctorService _doctors;
    private readonly PatientService _patients;
    private readonly CatalogService _catalog;
    private readonly CaseService _cases;
    private readonly BillingService _billing;
    private readonly PdfDocumentService _docs;

    public PdfDocumentServiceTests()
    {
        _conn.Open();
        var factory = new Factory(new DbContextOptionsBuilder<DentalLabDbContext>().UseSqlite(_conn).Options);
        using (var db = factory.CreateDbContext()) { db.Database.EnsureCreated(); CatalogSeeder.SeedAsync(db).GetAwaiter().GetResult(); }
        _doctors = new DoctorService(factory, new DoctorInputValidator());
        _patients = new PatientService(factory, new PatientInputValidator());
        _catalog = new CatalogService(factory, new CatalogItemInputValidator());
        _cases = new CaseService(factory, new CaseInputValidator());
        _billing = new BillingService(factory, new InvoiceInputValidator(), new PaymentInputValidator(), new AdjustmentInputValidator());

        var labs = new FixedLabs(new LabSettings(new LabProfile("معمل الابتسامة Smile Lab", "القاهرة - مصر", "0100 000 0000", "lab@example.com", null, "EGP"),
            PrintFormat.A4, DocLanguageMode.FollowApp));
        _docs = new PdfDocumentService(_billing, _doctors, _cases, labs, new DocumentFolders(Path.Combine(_dir, "Invoices"), Path.Combine(_dir, "Receipts")));
    }

    public void Dispose() { _conn.Dispose(); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private async Task<(int doctor, int invoice, int payment, int caseId)> SeedAsync()
    {
        var d = await _doctors.CreateAsync(new DoctorInput("د. أحمد علي", "عيادة الابتسامة", "0100 123 4567", null, "القاهرة", null, 50m, true));
        var p = await _patients.CreateAsync(new PatientInput("مريض تجريبي", d, null, null));
        var t = (await _catalog.GetRestorationTypesAsync(true)).First().Id;
        var c1 = await _cases.CreateAsync(new CaseInput(d, p, t, null, "A2", 3, 120m, 10m, DateTime.Today.AddDays(4), "ملاحظات", [11, 12, 21]));
        var c2 = await _cases.CreateAsync(new CaseInput(d, p, t, null, null, 1, 80m, 0m, DateTime.Today.AddDays(4), null, [55, 36]));
        var inv = await _billing.CreateInvoiceAsync(new InvoiceInput(d, [c1, c2], 15m, "فاتورة يونيو"));
        var pay = await _billing.RecordPaymentAsync(new PaymentInput(d, inv, DateTime.Today, 100m, PaymentMethod.Cash, "دفعة أولى"));
        return (d, inv, pay, c1);
    }

    private static void AssertPdf(PrintableDocument doc, string expectedName)
    {
        Assert.Equal(expectedName, doc.FileName);
        Assert.True(doc.Pdf.Length > 1500);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(doc.Pdf, 0, 5));
        Assert.NotEmpty(doc.Pages);
        Assert.All(doc.Pages, page => Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, page.Take(4).ToArray())); // PNG previews
    }

    public static IEnumerable<object[]> Combinations()
        => from f in Enum.GetValues<PrintFormat>() from l in new[] { AppLanguage.Arabic, AppLanguage.English } select new object[] { f, l };

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task AllFourDocuments_RenderInEveryFormatAndLanguage(PrintFormat format, AppLanguage language)
    {
        var (d, inv, pay, c) = await SeedAsync();
        var req = new DocumentRequest(format, language);

        var invoice = await _docs.CreateInvoiceAsync(inv, req);
        AssertPdf(invoice, $"INV-{DateTime.Now.Year}-000001.pdf");
        AssertPdf(await _docs.CreateReceiptAsync(pay, req), $"RC-{DateTime.Now.Year}-000001.pdf");
        AssertPdf(await _docs.CreateStatementAsync(d, req), $"statement-D-0001-{DateTime.Today:yyyyMMdd}.pdf");
        AssertPdf(await _docs.CreateWorkOrderAsync(c, req), $"workorder-C-{DateTime.Now.Year}-000001.pdf");
    }

    [Fact]
    public async Task InvoicesAndReceipts_AreArchivedAsPdfFiles_ButOtherDocumentsAreNot()
    {
        var (d, inv, pay, c) = await SeedAsync();
        var req = new DocumentRequest(PrintFormat.A4, AppLanguage.English);
        await _docs.CreateInvoiceAsync(inv, req);
        await _docs.CreateReceiptAsync(pay, req);
        await _docs.CreateStatementAsync(d, req);
        await _docs.CreateWorkOrderAsync(c, req);

        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "Invoices"), "*.pdf"));
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "Receipts"), "*.pdf"));
        Assert.Equal(2, Directory.GetFiles(_dir, "*.pdf", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task VoidedInvoices_AndReversedReceipts_StillPrint_ForTheRecord()
    {
        var (_, inv, pay, _) = await SeedAsync();
        await _billing.ReversePaymentAsync(pay, null);
        await _billing.VoidInvoiceAsync(inv, "test");
        var req = new DocumentRequest(PrintFormat.A5, AppLanguage.Arabic);
        AssertPdf(await _docs.CreateInvoiceAsync(inv, req), $"INV-{DateTime.Now.Year}-000001.pdf");
        AssertPdf(await _docs.CreateReceiptAsync(pay, req), $"RC-{DateTime.Now.Year}-000001.pdf");
    }

    [Fact]
    public async Task LongStatements_FlowOntoSeveralPages_OnA4()
    {
        var (d, _, _, _) = await SeedAsync();
        for (var i = 1; i <= 70; i++) await _billing.AddAdjustmentAsync(new AdjustmentInput(d, i % 2 == 0 ? 5m : -3m, "adjustment " + i));
        var doc = await _docs.CreateStatementAsync(d, new DocumentRequest(PrintFormat.A4, AppLanguage.English));
        Assert.True(doc.Pages.Count >= 2);
    }

    [Fact]
    public async Task MissingRecords_ThrowClearly()
    {
        var req = new DocumentRequest(PrintFormat.A4, AppLanguage.English);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _docs.CreateInvoiceAsync(999, req));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _docs.CreateReceiptAsync(999, req));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _docs.CreateStatementAsync(999, req));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _docs.CreateWorkOrderAsync(999, req));
    }
}
