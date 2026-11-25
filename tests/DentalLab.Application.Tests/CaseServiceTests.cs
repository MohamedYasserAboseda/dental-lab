using DentalLab.Application.Abstractions;
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

/// <summary>Real services on an in-memory SQLite database (migrations bypassed via EnsureCreated).</summary>
public sealed class CaseServiceTests : IDisposable
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

    public CaseServiceTests()
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
    }

    public void Dispose() => _conn.Dispose();

    private async Task<(int doctor, int patient, int type)> SeedAsync(string doctorName = "Dr A")
    {
        var d = await _doctors.CreateAsync(new DoctorInput(doctorName, null, null, null, null, null, 0, true));
        var p = await _patients.CreateAsync(new PatientInput("Patient " + doctorName, d, null, null));
        var t = (await _catalog.GetRestorationTypesAsync(true)).First().Id;
        return (d, p, t);
    }

    private static CaseInput Input(int d, int p, int t, int qty = 1, decimal price = 100m, decimal discount = 0m, int[]? teeth = null)
        => new(d, p, t, null, "A2", qty, price, discount, DateTime.Today.AddDays(5), null, teeth ?? [11]);

    [Fact]
    public async Task Seed_CreatesInitialRestorationTypesAndMaterials()
    {
        Assert.Equal(11, (await _catalog.GetRestorationTypesAsync(false)).Count);
        Assert.Equal(6, (await _catalog.GetMaterialsAsync(false)).Count);
    }

    [Fact]
    public async Task Create_GeneratesSequentialUniqueNumbers()
    {
        var (d, p, t) = await SeedAsync();
        var a = await _cases.CreateAsync(Input(d, p, t));
        var b = await _cases.CreateAsync(Input(d, p, t));
        var ca = await _cases.GetAsync(a);
        var cb = await _cases.GetAsync(b);

        Assert.Equal($"C-{DateTime.Now.Year}-000001", ca!.CaseNumber);
        Assert.Equal($"C-{DateTime.Now.Year}-000002", cb!.CaseNumber);
    }

    [Fact]
    public async Task Create_StoresTotalTeethHistoryAndAudit()
    {
        var (d, p, t) = await SeedAsync();
        var id = await _cases.CreateAsync(Input(d, p, t, qty: 3, price: 50.5m, discount: 10m, teeth: [12, 11, 13]));
        var c = await _cases.GetAsync(id);

        Assert.Equal(141.5m, c!.Total);
        Assert.Equal(new[] { 11, 12, 13 }, c.Teeth);
        Assert.Equal(CaseStatus.Received, c.Status);
        Assert.Single(c.History);
        await using var db = _factory.CreateDbContext();
        Assert.Contains(db.AuditLogs, a => a.Action == "CaseCreated");
    }

    [Fact]
    public async Task Create_PricingIsASnapshot_LaterPriceChangesDoNotAffectIt()
    {
        var (d, p, t) = await SeedAsync();
        var type = (await _catalog.GetRestorationTypesAsync(true)).First(x => x.Id == t);
        await _catalog.SaveRestorationTypeAsync(t, new CatalogItemInput(type.Name, type.NameAr, 80m, true));

        var price = await _catalog.ResolveUnitPriceAsync(d, t);
        var id = await _cases.CreateAsync(Input(d, p, t, price: price));
        await _catalog.SaveRestorationTypeAsync(t, new CatalogItemInput(type.Name, type.NameAr, 999m, true));

        Assert.Equal(80m, (await _cases.GetAsync(id))!.UnitPrice);
    }

    [Fact]
    public async Task ResolvePrice_PrefersDoctorSpecificPrice_ThenFallsBackToDefault()
    {
        var (d, _, t) = await SeedAsync();
        var type = (await _catalog.GetRestorationTypesAsync(true)).First(x => x.Id == t);
        await _catalog.SaveRestorationTypeAsync(t, new CatalogItemInput(type.Name, type.NameAr, 80m, true));
        Assert.Equal(80m, await _catalog.ResolveUnitPriceAsync(d, t));

        await _catalog.SetDoctorPricesAsync(d, new Dictionary<int, decimal?> { [t] = 65.5m });
        Assert.Equal(65.5m, await _catalog.ResolveUnitPriceAsync(d, t));

        await _catalog.SetDoctorPricesAsync(d, new Dictionary<int, decimal?> { [t] = null });
        Assert.Equal(80m, await _catalog.ResolveUnitPriceAsync(d, t));
    }

    [Fact]
    public async Task Catalog_RejectsDuplicateNames()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _catalog.SaveRestorationTypeAsync(null, new CatalogItemInput("zirconia", null, 0, true)));
        Assert.Contains(ex.Errors, e => e.ErrorCode == "DuplicateName");
    }

    [Fact]
    public async Task Create_PatientOfAnotherDoctor_IsRejected()
    {
        var (d1, p1, t) = await SeedAsync("Dr One");
        var (d2, _, _) = await SeedAsync("Dr Two");
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _cases.CreateAsync(Input(d2, p1, t)));
        Assert.Contains(ex.Errors, e => e.ErrorCode == "PatientMismatch");
    }

    [Fact]
    public async Task Create_InvalidInput_SavesNothing()
    {
        var (d, p, t) = await SeedAsync();
        await Assert.ThrowsAsync<ValidationException>(() => _cases.CreateAsync(Input(d, p, t, qty: 1, price: 10m, discount: 50m)));
        await Assert.ThrowsAsync<ValidationException>(() => _cases.CreateAsync(Input(d, p, t, teeth: [11, 99])));
        Assert.Equal(0, (await _cases.SearchAsync(new CaseQuery(null, null, null, null, null, false, 1, 10))).TotalCount);
    }

    [Fact]
    public async Task ChangeStatus_ValidTransition_IsPersistedWithHistory()
    {
        var (d, p, t) = await SeedAsync();
        var id = await _cases.CreateAsync(Input(d, p, t));
        await _cases.ChangeStatusAsync(id, CaseStatus.Designing, "design started");
        await _cases.ChangeStatusAsync(id, CaseStatus.Production, null);

        var c = await _cases.GetAsync(id);
        Assert.Equal(CaseStatus.Production, c!.Status);
        Assert.Equal(3, c.History.Count);
        Assert.Equal("design started", c.History[1].Note);
        Assert.Equal(CaseStatus.Designing, c.History[2].From);
    }

    [Fact]
    public async Task ChangeStatus_InvalidTransition_IsRejectedAndNotPersisted()
    {
        var (d, p, t) = await SeedAsync();
        var id = await _cases.CreateAsync(Input(d, p, t));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _cases.ChangeStatusAsync(id, CaseStatus.Delivered, null));
        Assert.Contains(ex.Errors, e => e.ErrorCode == "InvalidTransition");
        var c = await _cases.GetAsync(id);
        Assert.Equal(CaseStatus.Received, c!.Status);
        Assert.Single(c.History);
    }

    [Fact]
    public async Task Update_ReplacesTeethAndTotal_ButDeliveredCasesAreLocked()
    {
        var (d, p, t) = await SeedAsync();
        var id = await _cases.CreateAsync(Input(d, p, t, teeth: [11, 12]));
        await _cases.UpdateAsync(id, Input(d, p, t, qty: 2, price: 10m, teeth: [21]));
        var c = await _cases.GetAsync(id);
        Assert.Equal(new[] { 21 }, c!.Teeth);
        Assert.Equal(20m, c.Total);

        await _cases.ChangeStatusAsync(id, CaseStatus.Cancelled, null);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _cases.UpdateAsync(id, Input(d, p, t)));
        Assert.Contains(ex.Errors, e => e.ErrorCode == "NotEditable");
    }

    [Fact]
    public async Task Search_FiltersByStatusDoctorTermAndOverdue()
    {
        var (d1, p1, t) = await SeedAsync("Dr One");
        var (d2, p2, _) = await SeedAsync("Dr Two");
        var late = await _cases.CreateAsync(Input(d1, p1, t) with { ExpectedDeliveryDate = DateTime.Today.AddDays(-2) });
        await _cases.CreateAsync(Input(d1, p1, t));
        var other = await _cases.CreateAsync(Input(d2, p2, t));
        await _cases.ChangeStatusAsync(other, CaseStatus.Designing, null);

        Task<PagedResult<CaseListItem>> Q(string? term = null, int? doc = null, CaseStatus? st = null, bool over = false)
            => _cases.SearchAsync(new CaseQuery(term, doc, st, null, null, over, 1, 20));

        Assert.Equal(3, (await Q()).TotalCount);
        Assert.Equal(2, (await Q(doc: d1)).TotalCount);
        Assert.Equal(1, (await Q(st: CaseStatus.Designing)).TotalCount);
        var overdue = await Q(over: true);
        Assert.Equal(late, Assert.Single(overdue.Items).Id);
        Assert.True(overdue.Items[0].IsOverdue);
        Assert.Equal(1, (await Q(term: "Dr Two")).TotalCount);          // patient name
        Assert.Equal(1, (await Q(term: "000003")).TotalCount);          // case number
    }

    [Fact]
    public async Task Patients_ListCaseCount_AndBlockMovingPatientWithCases()
    {
        var (d, p, t) = await SeedAsync();
        await _cases.CreateAsync(Input(d, p, t));
        var other = await _doctors.CreateAsync(new DoctorInput("Dr B", null, null, null, null, null, 0, true));

        var list = await _patients.SearchAsync(new PatientQuery(null, d, 1, 10));
        Assert.Equal(1, Assert.Single(list.Items).CaseCount);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _patients.UpdateAsync(p, new PatientInput("Moved", other, null, null)));
        Assert.Contains(ex.Errors, e => e.ErrorCode == "PatientHasCases");
    }
}
