using DentalLab.Application.Abstractions;
using DentalLab.Application.Billing;
using DentalLab.Application.Cases;
using DentalLab.Application.Catalog;
using DentalLab.Application.Doctors;
using DentalLab.Application.Patients;
using DentalLab.Domain.Entities;
using DentalLab.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLab.Application.Tests;

/// <summary>The money flow end to end: case -> invoice -> payment -> ledger balance, on a real SQLite database.</summary>
public sealed class BillingServiceTests : IDisposable
{
    private sealed class Factory(DbContextOptions<DentalLabDbContext> o) : IDbContextFactory<DentalLabDbContext>
    {
        public DentalLabDbContext CreateDbContext() => new(o);
    }

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly Factory _factory;
    private readonly DoctorService _doctors;
    private readonly PatientService _patients;
    private readonly CatalogService _catalog;
    private readonly CaseService _cases;
    private readonly BillingService _billing;

    public BillingServiceTests()
    {
        _conn.Open();
        _factory = new Factory(new DbContextOptionsBuilder<DentalLabDbContext>().UseSqlite(_conn).Options);
        using (var db = _factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            CatalogSeeder.SeedAsync(db).GetAwaiter().GetResult();
        }
        _doctors = new DoctorService(_factory, new DoctorInputValidator());
        _patients = new PatientService(_factory, new PatientInputValidator());
        _catalog = new CatalogService(_factory, new CatalogItemInputValidator());
        _cases = new CaseService(_factory, new CaseInputValidator());
        _billing = new BillingService(_factory, new InvoiceInputValidator(), new PaymentInputValidator(), new AdjustmentInputValidator());
    }

    public void Dispose() => _conn.Dispose();

    private async Task<(int doctor, int patient, int type)> DoctorAsync(string name = "Dr A", decimal opening = 0)
    {
        var d = await _doctors.CreateAsync(new DoctorInput(name, null, null, null, null, null, opening, true));
        var p = await _patients.CreateAsync(new PatientInput("Pat " + name, d, null, null));
        return (d, p, (await _catalog.GetRestorationTypesAsync(true)).First().Id);
    }

    private Task<int> NewCaseAsync(int d, int p, int t, decimal price = 100m, int qty = 1)
        => _cases.CreateAsync(new CaseInput(d, p, t, null, null, qty, price, 0m, DateTime.Today.AddDays(3), null, [11]));

    private async Task<decimal> BalanceAsync(int doctor) => (await _doctors.GetAsync(doctor))!.Balance;

    private static PaymentInput Pay(int d, decimal amount, int? invoice = null, PaymentMethod m = PaymentMethod.Cash)
        => new(d, invoice, DateTime.Today, amount, m, null);

    [Fact]
    public async Task CreateInvoice_PostsOneLedgerCharge_LinksCases_AndNumbersSequentially()
    {
        var (d, p, t) = await DoctorAsync(opening: 50m);
        var c1 = await NewCaseAsync(d, p, t, 100m, 2);
        var c2 = await NewCaseAsync(d, p, t, 30m);

        var id = await _billing.CreateInvoiceAsync(new InvoiceInput(d, [c1, c2], 10m, "June"));
        var inv = await _billing.GetInvoiceAsync(id);

        Assert.Equal($"INV-{DateTime.Now.Year}-000001", inv!.Number);
        Assert.Equal(230m, inv.Subtotal);
        Assert.Equal(220m, inv.Total);
        Assert.Equal(2, inv.Lines.Count);
        Assert.Equal(270m, await BalanceAsync(d));                          // 50 opening + 220
        Assert.True((await _cases.GetAsync(c1))!.IsInvoiced);
        Assert.Empty(await _billing.GetUnbilledCasesAsync(d, null, null));

        var c3 = await NewCaseAsync(d, p, t);
        var inv2 = await _billing.GetInvoiceAsync(await _billing.CreateInvoiceAsync(new InvoiceInput(d, [c3], 0m, null)));
        Assert.Equal($"INV-{DateTime.Now.Year}-000002", inv2!.Number);
    }

    [Fact]
    public async Task CreateInvoice_CaseCannotBeBilledTwice()
    {
        var (d, p, t) = await DoctorAsync();
        var c = await NewCaseAsync(d, p, t);
        await _billing.CreateInvoiceAsync(new InvoiceInput(d, [c], 0m, null));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _billing.CreateInvoiceAsync(new InvoiceInput(d, [c], 0m, null)));
        Assert.Contains(ex.Errors, e => e.ErrorCode == "AlreadyInvoiced");
        Assert.Equal(100m, await BalanceAsync(d));
    }

    [Fact]
    public async Task CreateInvoice_RejectsOtherDoctorsCases_CancelledCases_ZeroTotals_AndBadDiscount()
    {
        var (d1, p1, t) = await DoctorAsync("Dr One");
        var (d2, p2, _) = await DoctorAsync("Dr Two");
        var other = await NewCaseAsync(d2, p2, t);
        var cancelled = await NewCaseAsync(d1, p1, t);
        await _cases.ChangeStatusAsync(cancelled, CaseStatus.Cancelled, null);
        var free = await NewCaseAsync(d1, p1, t, price: 0m);
        var normal = await NewCaseAsync(d1, p1, t);

        async Task<string> Code(InvoiceInput i)
            => (await Assert.ThrowsAsync<ValidationException>(() => _billing.CreateInvoiceAsync(i))).Errors.First().ErrorCode;

        Assert.Equal("CaseNotFound", await Code(new InvoiceInput(d1, [other], 0m, null)));
        Assert.Equal("CaseCancelled", await Code(new InvoiceInput(d1, [cancelled], 0m, null)));
        Assert.Equal("InvoiceTotalInvalid", await Code(new InvoiceInput(d1, [free], 0m, null)));
        Assert.Equal("DiscountInvalid", await Code(new InvoiceInput(d1, [normal], 500m, null)));
        Assert.Equal("CasesRequired", await Code(new InvoiceInput(d1, [], 0m, null)));
        Assert.Equal(0m, await BalanceAsync(d1));
    }

    [Fact]
    public async Task Payment_ReducesBalanceAndInvoiceRemaining_AndGetsAReceiptNumber()
    {
        var (d, p, t) = await DoctorAsync();
        var inv = await _billing.CreateInvoiceAsync(new InvoiceInput(d, [await NewCaseAsync(d, p, t, 100m)], 0m, null));

        await _billing.RecordPaymentAsync(Pay(d, 40m, inv));
        var details = await _billing.GetInvoiceAsync(inv);
        Assert.Equal(InvoiceStatus.PartiallyPaid, details!.Status);
        Assert.Equal(60m, details.Remaining);
        Assert.Equal(60m, await BalanceAsync(d));
        Assert.Equal($"RC-{DateTime.Now.Year}-000001", Assert.Single(details.Payments).ReceiptNumber);

        await _billing.RecordPaymentAsync(Pay(d, 60m, inv, PaymentMethod.Card));
        Assert.Equal(InvoiceStatus.Paid, (await _billing.GetInvoiceAsync(inv))!.Status);
        Assert.Equal(0m, await BalanceAsync(d));
        Assert.Empty(await _billing.GetOpenInvoicesAsync(d));
    }

    [Fact]
    public async Task Payment_Overpayment_IsRejected_AndNothingIsSaved()
    {
        var (d, p, t) = await DoctorAsync();
        var inv = await _billing.CreateInvoiceAsync(new InvoiceInput(d, [await NewCaseAsync(d, p, t, 100m)], 0m, null));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _billing.RecordPaymentAsync(Pay(d, 100.01m, inv)));
        Assert.Contains(ex.Errors, e => e.ErrorCode == "Overpayment");
        Assert.Equal(100m, await BalanceAsync(d));
        Assert.Equal(0, (await _billing.SearchPaymentsAsync(new PaymentQuery(null, null, 1, 10))).TotalCount);
    }

    [Fact]
    public async Task Payment_DuplicateWithinAMinute_IsRejected_ButDifferentAmountOrMethodIsFine()
    {
        var (d, _, _) = await DoctorAsync();
        await _billing.RecordPaymentAsync(Pay(d, 25m));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _billing.RecordPaymentAsync(Pay(d, 25m)));
        Assert.Contains(ex.Errors, e => e.ErrorCode == "DuplicatePayment");

        await _billing.RecordPaymentAsync(Pay(d, 26m));
        await _billing.RecordPaymentAsync(Pay(d, 25m, null, PaymentMethod.Card));
        Assert.Equal(-76m, await BalanceAsync(d)); // credit balance (paid on account)
    }

    [Fact]
    public async Task Payment_InvalidInput_IsRejected()
    {
        var (d, _, _) = await DoctorAsync();
        await Assert.ThrowsAsync<ValidationException>(() => _billing.RecordPaymentAsync(Pay(d, 0m)));
        await Assert.ThrowsAsync<ValidationException>(() => _billing.RecordPaymentAsync(Pay(d, -5m)));
        await Assert.ThrowsAsync<ValidationException>(() => _billing.RecordPaymentAsync(Pay(0, 5m)));
        Assert.Equal(0m, await BalanceAsync(d));
    }

    [Fact]
    public async Task ReversePayment_RestoresBalanceAndInvoice_AndKeepsHistory()
    {
        var (d, p, t) = await DoctorAsync();
        var inv = await _billing.CreateInvoiceAsync(new InvoiceInput(d, [await NewCaseAsync(d, p, t, 100m)], 0m, null));
        var pay = await _billing.RecordPaymentAsync(Pay(d, 100m, inv));

        await _billing.ReversePaymentAsync(pay, "bounced");
        Assert.Equal(100m, await BalanceAsync(d));
        Assert.Equal(InvoiceStatus.Unpaid, (await _billing.GetInvoiceAsync(inv))!.Status);

        var statement = await _doctors.GetStatementAsync(d);
        Assert.Equal(3, statement.Count);                                    // charge, payment, reversal: nothing deleted
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _billing.ReversePaymentAsync(pay, null));
        Assert.Contains(ex.Errors, e => e.ErrorCode == "AlreadyReversed");
        Assert.True((await _billing.SearchPaymentsAsync(new PaymentQuery(null, null, 1, 10))).Items.Single().IsReversed);
    }

    [Fact]
    public async Task VoidInvoice_ReversesCharge_ReleasesCases_AndBlocksWhenPaid()
    {
        var (d, p, t) = await DoctorAsync();
        var c = await NewCaseAsync(d, p, t, 100m);
        var inv = await _billing.CreateInvoiceAsync(new InvoiceInput(d, [c], 0m, null));

        var pay = await _billing.RecordPaymentAsync(Pay(d, 10m, inv));
        var blocked = await Assert.ThrowsAsync<ValidationException>(() => _billing.VoidInvoiceAsync(inv, null));
        Assert.Contains(blocked.Errors, e => e.ErrorCode == "InvoiceHasPayments");

        await _billing.ReversePaymentAsync(pay, null);
        await _billing.VoidInvoiceAsync(inv, "wrong prices");

        Assert.Equal(0m, await BalanceAsync(d));
        Assert.Equal(InvoiceStatus.Voided, (await _billing.GetInvoiceAsync(inv))!.Status);
        Assert.False((await _cases.GetAsync(c))!.IsInvoiced);
        Assert.Single(await _billing.GetUnbilledCasesAsync(d, null, null));   // billable again

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _billing.RecordPaymentAsync(Pay(d, 5m, inv)));
        Assert.Contains(ex.Errors, e => e.ErrorCode == "InvoiceVoided");
    }

    [Fact]
    public async Task InvoicedCase_CannotBeEditedOrCancelled()
    {
        var (d, p, t) = await DoctorAsync();
        var c = await NewCaseAsync(d, p, t);
        await _billing.CreateInvoiceAsync(new InvoiceInput(d, [c], 0m, null));

        var edit = await Assert.ThrowsAsync<ValidationException>(() =>
            _cases.UpdateAsync(c, new CaseInput(d, p, t, null, null, 1, 1m, 0m, DateTime.Today, null, [11])));
        Assert.Contains(edit.Errors, e => e.ErrorCode == "CaseInvoiced");
        var cancel = await Assert.ThrowsAsync<ValidationException>(() => _cases.ChangeStatusAsync(c, CaseStatus.Cancelled, null));
        Assert.Contains(cancel.Errors, e => e.ErrorCode == "CaseInvoiced");
        await _cases.ChangeStatusAsync(c, CaseStatus.Designing, null); // production continues
    }

    [Fact]
    public async Task Adjustment_AffectsBalance_NeedsDescription_AndIsInTheStatement()
    {
        var (d, _, _) = await DoctorAsync(opening: 100m);
        await _billing.AddAdjustmentAsync(new AdjustmentInput(d, -15.5m, "goodwill discount"));
        Assert.Equal(84.5m, await BalanceAsync(d));

        await Assert.ThrowsAsync<ValidationException>(() => _billing.AddAdjustmentAsync(new AdjustmentInput(d, 10m, " ")));
        await Assert.ThrowsAsync<ValidationException>(() => _billing.AddAdjustmentAsync(new AdjustmentInput(d, 0m, "zero")));
        Assert.Equal(LedgerEntryType.Adjustment, (await _doctors.GetStatementAsync(d)).Last().Type);
    }

    [Fact]
    public async Task Search_FiltersInvoicesByDoctorStatusAndTerm_AndUnbilledByDate()
    {
        var (d1, p1, t) = await DoctorAsync("Dr One");
        var (d2, p2, _) = await DoctorAsync("Dr Two");
        var i1 = await _billing.CreateInvoiceAsync(new InvoiceInput(d1, [await NewCaseAsync(d1, p1, t)], 0m, null));
        var i2 = await _billing.CreateInvoiceAsync(new InvoiceInput(d2, [await NewCaseAsync(d2, p2, t)], 0m, null));
        await _billing.RecordPaymentAsync(Pay(d2, 100m, i2));

        InvoiceQuery Q(string? term = null, int? doc = null, InvoiceStatus? st = null) => new(term, doc, st, 1, 20);
        Assert.Equal(2, (await _billing.SearchInvoicesAsync(Q())).TotalCount);
        Assert.Equal(i1, Assert.Single((await _billing.SearchInvoicesAsync(Q(doc: d1))).Items).Id);
        Assert.Equal(i2, Assert.Single((await _billing.SearchInvoicesAsync(Q(st: InvoiceStatus.Paid))).Items).Id);
        Assert.Equal(1, (await _billing.SearchInvoicesAsync(Q(term: "Dr Two"))).TotalCount);
        Assert.Equal(1, (await _billing.SearchInvoicesAsync(Q(term: "000001"))).TotalCount);

        var c = await NewCaseAsync(d1, p1, t);
        Assert.Single(await _billing.GetUnbilledCasesAsync(d1, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
        Assert.Empty(await _billing.GetUnbilledCasesAsync(d1, DateTime.UtcNow.AddDays(1), null));
        Assert.Equal(c, (await _billing.GetUnbilledCasesAsync(d1, null, null)).Single().CaseId);
    }
}
