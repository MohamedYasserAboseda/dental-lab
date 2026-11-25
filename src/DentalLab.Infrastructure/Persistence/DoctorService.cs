using DentalLab.Application;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Doctors;
using DentalLab.Domain;
using DentalLab.Domain.Entities;
using DentalLab.Domain.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DentalLab.Infrastructure.Persistence;

public sealed class DoctorService(
    IDbContextFactory<DentalLabDbContext> factory,
    IValidator<DoctorInput> validator) : IDoctorService
{
    public async Task<PagedResult<DoctorListItem>> SearchAsync(DoctorQuery q, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<Doctor> query = db.Doctors.AsNoTracking();

        query = q.Status switch
        {
            DoctorStatusFilter.Active => query.Where(d => d.IsActive),
            DoctorStatusFilter.Inactive => query.Where(d => !d.IsActive),
            _ => query
        };

        var term = q.Term?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var like = $"%{EscapeLike(term)}%";
            int? idFromCode = TryParseCode(term);
            query = query.Where(d =>
                EF.Functions.Like(d.FullName, like, "\\") ||
                (d.ClinicName != null && EF.Functions.Like(d.ClinicName, like, "\\")) ||
                (d.Phone != null && EF.Functions.Like(d.Phone, like, "\\")) ||
                (idFromCode != null && d.Id == idFromCode));
        }

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var doctors = await query.OrderBy(d => d.FullName)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);

        var ids = doctors.Select(d => d.Id).ToList();
        // Decimal SUM is not translatable by the SQLite provider, so sum in memory (via LedgerCalculator).
        var amounts = await db.LedgerEntries.AsNoTracking()
            .Where(l => ids.Contains(l.DoctorId))
            .Select(l => new { l.DoctorId, l.Amount }).ToListAsync(ct);
        var balances = amounts.GroupBy(a => a.DoctorId)
            .ToDictionary(g => g.Key, g => LedgerCalculator.Balance(g.Select(x => x.Amount)));

        var items = doctors.Select(d => new DoctorListItem(
            d.Id, d.Code, d.FullName, d.ClinicName, d.Phone, d.IsActive,
            balances.GetValueOrDefault(d.Id))).ToList();

        return new PagedResult<DoctorListItem>(items, total);
    }

    public async Task<DoctorDetails?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var d = await db.Doctors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return null;

        var amounts = await db.LedgerEntries.AsNoTracking()
            .Where(l => l.DoctorId == id).Select(l => l.Amount).ToListAsync(ct);

        return new DoctorDetails(d.Id, d.Code, d.FullName, d.ClinicName, d.Phone, d.Email, d.Address,
            d.Notes, d.RegisteredUtc, d.OpeningBalance, d.IsActive, LedgerCalculator.Balance(amounts));
    }

    public async Task<int> CreateAsync(DoctorInput input, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(input, ct);

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var doctor = Doctor.Create(input.FullName, input.ClinicName, input.Phone, input.Email,
            input.Address, input.Notes, input.OpeningBalance, input.IsActive);
        db.Doctors.Add(doctor); // opening ledger entry is saved in the same unit of work
        await db.SaveChangesAsync(ct);

        db.AuditLogs.Add(AuditLog.Create("DoctorCreated", nameof(Doctor), doctor.Id.ToString(),
            $"OpeningBalance={doctor.OpeningBalance}"));
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return doctor.Id;
    }

    public async Task UpdateAsync(int id, DoctorInput input, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(input, ct);

        await using var db = await factory.CreateDbContextAsync(ct);
        var doctor = await db.Doctors.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Doctor {id} not found.");

        doctor.UpdateDetails(input.FullName, input.ClinicName, input.Phone, input.Email,
            input.Address, input.Notes, input.IsActive);
        db.AuditLogs.Add(AuditLog.Create("DoctorUpdated", nameof(Doctor), id.ToString()));
        await db.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(int id, bool isActive, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var doctor = await db.Doctors.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Doctor {id} not found.");

        doctor.SetActive(isActive);
        db.AuditLogs.Add(AuditLog.Create(isActive ? "DoctorActivated" : "DoctorDeactivated",
            nameof(Doctor), id.ToString()));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<StatementLine>> GetStatementAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entries = await db.LedgerEntries.AsNoTracking().Where(l => l.DoctorId == id).ToListAsync(ct);
        return LedgerCalculator.BuildStatement(entries);
    }

    public async Task<IReadOnlyList<LookupItem>> LookupAsync(bool activeOnly, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Doctors.AsNoTracking();
        if (activeOnly) q = q.Where(d => d.IsActive);
        return await q.OrderBy(d => d.FullName).Select(d => new LookupItem(d.Id, d.FullName)).ToListAsync(ct);
    }

    private static string EscapeLike(string s)
        => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static int? TryParseCode(string term)
    {
        var t = term.StartsWith("D-", StringComparison.OrdinalIgnoreCase) ? term[2..] : term;
        return int.TryParse(t, out var id) && id > 0 ? id : null;
    }
}
