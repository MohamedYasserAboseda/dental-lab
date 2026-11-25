using DentalLab.Application.Doctors;
using DentalLab.Domain.Entities;
using DentalLab.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLab.Application.Tests;

/// <summary>Runs the real DoctorService against an in-memory SQLite database.</summary>
public sealed class DoctorServiceTests : IDisposable
{
    private sealed class Factory(DbContextOptions<DentalLabDbContext> o) : IDbContextFactory<DentalLabDbContext>
    {
        public DentalLabDbContext CreateDbContext() => new(o);
    }

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly DoctorService _svc;
    private readonly Factory _factory;

    public DoctorServiceTests()
    {
        _conn.Open();
        var options = new DbContextOptionsBuilder<DentalLabDbContext>().UseSqlite(_conn).Options;
        _factory = new Factory(options);
        using (var db = _factory.CreateDbContext()) db.Database.EnsureCreated();
        _svc = new DoctorService(_factory, new DoctorInputValidator());
    }

    public void Dispose() => _conn.Dispose();

    private static DoctorInput Input(string name, decimal opening = 0, bool active = true, string? clinic = null)
        => new(name, clinic, null, null, null, null, opening, active);

    [Fact]
    public async Task Create_WithOpeningBalance_BalanceAndStatementMatch()
    {
        var id = await _svc.CreateAsync(Input("Ahmed", 150.505m));
        var d = await _svc.GetAsync(id);
        Assert.Equal(150.51m, d!.Balance);

        var statement = await _svc.GetStatementAsync(id);
        var line = Assert.Single(statement);
        Assert.Equal(LedgerEntryType.OpeningBalance, line.Type);
        Assert.Equal(150.51m, line.RunningBalance);
    }

    [Fact]
    public async Task Create_InvalidInput_ThrowsAndSavesNothing()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _svc.CreateAsync(Input("")));
        var result = await _svc.SearchAsync(new DoctorQuery(null, DoctorStatusFilter.All, 1, 10));
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Create_WritesAuditLog()
    {
        await _svc.CreateAsync(Input("Ahmed"));
        await using var db = _factory.CreateDbContext();
        Assert.Contains(db.AuditLogs, a => a.Action == "DoctorCreated");
    }

    [Fact]
    public async Task Search_FiltersByTermAndStatus_AndPages()
    {
        await _svc.CreateAsync(Input("Ahmed Ali", 10, clinic: "Smile Clinic"));
        var inactive = await _svc.CreateAsync(Input("Ahmed Hassan", 20));
        await _svc.CreateAsync(Input("Mona", 30));
        await _svc.SetActiveAsync(inactive, false);

        var byName = await _svc.SearchAsync(new DoctorQuery("ahmed", DoctorStatusFilter.All, 1, 10));
        Assert.Equal(2, byName.TotalCount);

        var byClinic = await _svc.SearchAsync(new DoctorQuery("smile", DoctorStatusFilter.All, 1, 10));
        Assert.Single(byClinic.Items);

        var active = await _svc.SearchAsync(new DoctorQuery(null, DoctorStatusFilter.Active, 1, 10));
        Assert.Equal(2, active.TotalCount);

        var onlyInactive = await _svc.SearchAsync(new DoctorQuery(null, DoctorStatusFilter.Inactive, 1, 10));
        Assert.Equal("Ahmed Hassan", Assert.Single(onlyInactive.Items).FullName);

        var page2 = await _svc.SearchAsync(new DoctorQuery(null, DoctorStatusFilter.All, 2, 2));
        Assert.Single(page2.Items);
        Assert.Equal(3, page2.TotalCount);
    }

    [Fact]
    public async Task Search_ByCode_FindsDoctor()
    {
        var id = await _svc.CreateAsync(Input("Mona"));
        var r = await _svc.SearchAsync(new DoctorQuery($"D-{id:0000}", DoctorStatusFilter.All, 1, 10));
        Assert.Equal(id, Assert.Single(r.Items).Id);
    }

    [Fact]
    public async Task Update_ChangesDetails_ButNeverTouchesLedger()
    {
        var id = await _svc.CreateAsync(Input("Ahmed", 100));
        await _svc.UpdateAsync(id, Input("Ahmed Updated", opening: 999999));

        var d = await _svc.GetAsync(id);
        Assert.Equal("Ahmed Updated", d!.FullName);
        Assert.Equal(100m, d.OpeningBalance);
        Assert.Equal(100m, d.Balance);
        Assert.Single(await _svc.GetStatementAsync(id));
    }

    [Fact]
    public async Task Search_ListsBalancePerDoctor()
    {
        await _svc.CreateAsync(Input("A", 10.10m));
        await _svc.CreateAsync(Input("B", -5m));
        var r = await _svc.SearchAsync(new DoctorQuery(null, DoctorStatusFilter.All, 1, 10));
        Assert.Equal(10.10m, r.Items.Single(x => x.FullName == "A").Balance);
        Assert.Equal(-5m, r.Items.Single(x => x.FullName == "B").Balance);
    }
}
