using DentalLab.Application.Billing;
using DentalLab.Application.Cases;
using DentalLab.Application.Catalog;
using DentalLab.Application.Dashboard;
using DentalLab.Application.Doctors;
using DentalLab.Application.Patients;
using DentalLab.Domain.Entities;
using DentalLab.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLab.Application.Tests;

public sealed class DashboardServiceTests : IDisposable
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
    private readonly DashboardService _dash;
    private static readonly DashboardQuery AllTime = new(null, null);

    public DashboardServiceTests()
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
        _dash = new DashboardService(_factory);
    }

    public void Dispose() => _conn.Dispose();

    private async Task<(int d, int p, int t)> DoctorAsync(string name, decimal opening = 0)
    {
        var d = await _doctors.CreateAsync(new DoctorInput(name, null, null, null, null, null, opening, true));
        var p = await _patients.CreateAsync(new PatientInput("Pat " + name, d, null, null));
        return (d, p, (await _catalog.GetRestorationTypesAsync(true)).First().Id);
    }

    private Task<int> CaseAsync(int d, int p, int t, decimal price = 100m, int daysToDeliver = 5)
        => _cases.CreateAsync(new CaseInput(d, p, t, null, null, 1, price, 0m, DateTime.Today.AddDays(daysToDeliver), null, [11]));

    [Fact]
    public async Task EmptyDatabase_ReturnsZerosAndEmptyLists_NotErrors()
    {
        var r = await _dash.GetAsync(AllTime);
        Assert.Equal(0, r.TotalCases);
        Assert.Equal(0m, r.OutstandingBalances);
        Assert.Equal(6, r.MonthlyBilled.Count);
        Assert.Equal(6, r.MonthlyCases.Count);
        Assert.All(r.StatusDistribution, s => Assert.Equal(0, s.Count));
        Assert.Empty(r.RecentPayments);
        Assert.Empty(r.UpcomingDeliveries);
    }

    [Fact]
    public async Task CaseCounters_ReflectRealCases()
    {
        var (d, p, t) = await DoctorAsync("Dr A");
        var late = await CaseAsync(d, p, t, daysToDeliver: -2);
        var inProd = await CaseAsync(d, p, t);
        var ready = await CaseAsync(d, p, t);
        var cancelled = await CaseAsync(d, p, t, daysToDeliver: -9);
        await _cases.ChangeStatusAsync(inProd, CaseStatus.Production, null);
        foreach (var s in new[] { CaseStatus.Production, CaseStatus.Finishing, CaseStatus.QualityControl, CaseStatus.Ready })
            await _cases.ChangeStatusAsync(ready, s, null);
        await _cases.ChangeStatusAsync(cancelled, CaseStatus.Cancelled, null);

        var r = await _dash.GetAsync(AllTime);
        Assert.Equal(1, r.TotalDoctors);
        Assert.Equal(4, r.TotalCases);
        Assert.Equal(4, r.ReceivedToday);
        Assert.Equal(1, r.InProduction);
        Assert.Equal(1, r.ReadyForDelivery);
        Assert.Equal(1, r.Overdue);                                // only the late one: cancelled cases are not overdue
        Assert.Equal(1, r.StatusDistribution.Single(s => s.Status == CaseStatus.Cancelled).Count);
        Assert.Equal(4, r.MonthlyCases.Last().Count);
        Assert.Equal(late, r.UpcomingDeliveries.First().CaseId);   // earliest due date first
        Assert.True(r.UpcomingDeliveries.First().IsOverdue);
        Assert.DoesNotContain(r.UpcomingDeliveries, u => u.CaseId == cancelled);
    }

    [Fact]
    public async Task Money_BilledCollectedAndOutstanding_MatchTheLedger()
    {
        var (d1, p1, t) = await DoctorAsync("Dr One", opening: 50m);
        var (d2, p2, _) = await DoctorAsync("Dr Two");
        var inv1 = await _billing.CreateInvoiceAsync(new InvoiceInput(d1, [await CaseAsync(d1, p1, t, 200m)], 0m, null));
        var inv2 = await _billing.CreateInvoiceAsync(new InvoiceInput(d2, [await CaseAsync(d2, p2, t, 80m)], 0m, null));
        await _billing.RecordPaymentAsync(new PaymentInput(d1, inv1, DateTime.Today, 120m, PaymentMethod.Cash, null));
        await _billing.RecordPaymentAsync(new PaymentInput(d2, inv2, DateTime.Today, 80m, PaymentMethod.Card, null));
        var wrong = await _billing.RecordPaymentAsync(new PaymentInput(d2, null, DateTime.Today, 999m, PaymentMethod.Cash, null));
        await _billing.ReversePaymentAsync(wrong, null);            // reversed: must not count

        var voidable = await _billing.CreateInvoiceAsync(new InvoiceInput(d2, [await CaseAsync(d2, p2, t, 55m)], 0m, null));
        await _billing.VoidInvoiceAsync(voidable, null);            // voided: must not count

        var r = await _dash.GetAsync(AllTime);
        Assert.Equal(280m, r.BilledToday);
        Assert.Equal(200m, r.CollectedToday);
        Assert.Equal(280m, r.BilledThisMonth);
        Assert.Equal(200m, r.CollectedThisMonth);
        Assert.Equal(280m, r.BilledInRange);
        Assert.Equal(200m, r.CollectedInRange);
        Assert.Equal(130m, r.OutstandingBalances);                  // Dr One: 50 + 200 - 120 = 130, Dr Two: 0
        Assert.Equal(280m, r.MonthlyBilled.Last().Value);
        Assert.Equal(200m, r.MonthlyCollected.Last().Value);
        Assert.Equal(0m, r.MonthlyBilled.First().Value);
        Assert.Equal(3, r.RecentPayments.Count);
        Assert.Contains(r.RecentPayments, x => x.IsReversed);
    }

    [Fact]
    public async Task CreditBalances_AreNotCountedAsOutstanding()
    {
        var (d, _, _) = await DoctorAsync("Dr Credit");
        await _billing.RecordPaymentAsync(new PaymentInput(d, null, DateTime.Today, 300m, PaymentMethod.Cash, null)); // paid on account
        Assert.Equal(0m, (await _dash.GetAsync(AllTime)).OutstandingBalances);
    }

    [Fact]
    public async Task DateRange_FiltersRangeFigures_ButNotTodayCards()
    {
        var (d, p, t) = await DoctorAsync("Dr A");
        await _billing.CreateInvoiceAsync(new InvoiceInput(d, [await CaseAsync(d, p, t, 100m)], 0m, null));

        var future = new DashboardQuery(DateTime.Today.AddDays(10), DateTime.Today.AddDays(20));
        var r = await _dash.GetAsync(future);
        Assert.Equal(0m, r.BilledInRange);
        Assert.All(r.StatusDistribution, s => Assert.Equal(0, s.Count));
        Assert.Empty(r.TopDoctors);
        Assert.Equal(100m, r.BilledToday);                          // cards for "today" ignore the range
        Assert.Equal(1, r.TotalCases);

        var current = await _dash.GetAsync(new DashboardQuery(DateTime.Today, DateTime.Today.AddDays(1)));
        Assert.Equal(100m, current.BilledInRange);
        Assert.Equal(1, current.StatusDistribution.Single(s => s.Status == CaseStatus.Received).Count);
    }

    [Fact]
    public async Task TopDoctors_AreRankedByCaseVolume_AndLimitedToFive()
    {
        for (var i = 1; i <= 6; i++)
        {
            var (d, p, t) = await DoctorAsync("Doctor " + i);
            for (var c = 0; c < i; c++) await CaseAsync(d, p, t);
        }
        var top = (await _dash.GetAsync(AllTime)).TopDoctors;
        Assert.Equal(5, top.Count);
        Assert.Equal("Doctor 6", top[0].Name);
        Assert.Equal(6, top[0].Count);
        Assert.Equal(new[] { 6, 5, 4, 3, 2 }, top.Select(x => x.Count).ToArray());
    }

    [Fact]
    public async Task RecentActivity_ListsLatestStatusChangesWithCaseNumbers()
    {
        var (d, p, t) = await DoctorAsync("Dr A");
        var c = await CaseAsync(d, p, t);
        await _cases.ChangeStatusAsync(c, CaseStatus.Designing, null);

        var activity = (await _dash.GetAsync(AllTime)).RecentActivity;
        Assert.Equal(2, activity.Count);                            // Received + Designing
        Assert.Equal(CaseStatus.Designing, activity[0].To);
        Assert.StartsWith("C-", activity[0].CaseNumber);
    }
}
