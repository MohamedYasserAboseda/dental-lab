using DentalLab.Application.Abstractions;
using DentalLab.Application.Catalog;
using DentalLab.Domain;
using DentalLab.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace DentalLab.Infrastructure.Persistence;

public sealed class CatalogService(
    IDbContextFactory<DentalLabDbContext> factory,
    IValidator<CatalogItemInput> validator) : ICatalogService
{
    public async Task<IReadOnlyList<CatalogItem>> GetRestorationTypesAsync(bool activeOnly, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.RestorationTypes.AsNoTracking();
        if (activeOnly) q = q.Where(x => x.IsActive);
        var rows = await q.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct);
        return rows.Select(x => new CatalogItem(x.Id, x.Name, x.NameAr, x.DefaultUnitPrice, x.IsActive)).ToList();
    }

    public async Task<IReadOnlyList<CatalogItem>> GetMaterialsAsync(bool activeOnly, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Materials.AsNoTracking();
        if (activeOnly) q = q.Where(x => x.IsActive);
        var rows = await q.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct);
        return rows.Select(x => new CatalogItem(x.Id, x.Name, x.NameAr, 0m, x.IsActive)).ToList();
    }

    public async Task<int> SaveRestorationTypeAsync(int? id, CatalogItemInput input, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(input, ct);
        await using var db = await factory.CreateDbContextAsync(ct);

        var name = input.Name.Trim().ToLower();
        if (await db.RestorationTypes.AnyAsync(x => x.Id != (id ?? 0) && x.Name.ToLower() == name, ct))
            throw Fail("Name", "DuplicateName");

        RestorationType entity;
        if (id is int existingId)
            entity = await db.RestorationTypes.FirstOrDefaultAsync(x => x.Id == existingId, ct)
                ?? throw new InvalidOperationException("Restoration type not found.");
        else
        {
            var next = (await db.RestorationTypes.Select(x => (int?)x.SortOrder).MaxAsync(ct) ?? -1) + 1;
            entity = RestorationType.Create(input.Name, input.NameAr, input.DefaultPrice, input.IsActive, next);
            db.RestorationTypes.Add(entity);
        }
        if (id is not null) entity.Update(input.Name, input.NameAr, input.DefaultPrice, input.IsActive);

        db.AuditLogs.Add(AuditLog.Create(id is null ? "RestorationTypeCreated" : "RestorationTypeUpdated",
            nameof(RestorationType), id?.ToString(), $"Price={Money.Round(input.DefaultPrice)}"));
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<int> SaveMaterialAsync(int? id, CatalogItemInput input, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(input, ct);
        await using var db = await factory.CreateDbContextAsync(ct);

        var name = input.Name.Trim().ToLower();
        if (await db.Materials.AnyAsync(x => x.Id != (id ?? 0) && x.Name.ToLower() == name, ct))
            throw Fail("Name", "DuplicateName");

        Material entity;
        if (id is int existingId)
        {
            entity = await db.Materials.FirstOrDefaultAsync(x => x.Id == existingId, ct)
                ?? throw new InvalidOperationException("Material not found.");
            entity.Update(input.Name, input.NameAr, input.IsActive);
        }
        else
        {
            var next = (await db.Materials.Select(x => (int?)x.SortOrder).MaxAsync(ct) ?? -1) + 1;
            entity = Material.Create(input.Name, input.NameAr, input.IsActive, next);
            db.Materials.Add(entity);
        }

        db.AuditLogs.Add(AuditLog.Create(id is null ? "MaterialCreated" : "MaterialUpdated", nameof(Material), id?.ToString()));
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<decimal> ResolveUnitPriceAsync(int doctorId, int restorationTypeId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // decimals are read as values and compared in memory (SQLite cannot order/aggregate decimals in SQL)
        var special = await db.DoctorPrices.AsNoTracking()
            .Where(p => p.DoctorId == doctorId && p.RestorationTypeId == restorationTypeId)
            .Select(p => (decimal?)p.UnitPrice).FirstOrDefaultAsync(ct);
        if (special is not null) return special.Value;

        return await db.RestorationTypes.AsNoTracking().Where(r => r.Id == restorationTypeId)
            .Select(r => r.DefaultUnitPrice).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<DoctorPriceItem>> GetDoctorPricesAsync(int doctorId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var types = await db.RestorationTypes.AsNoTracking().Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);
        var prices = await db.DoctorPrices.AsNoTracking().Where(p => p.DoctorId == doctorId).ToListAsync(ct);
        var map = prices.ToDictionary(p => p.RestorationTypeId, p => p.UnitPrice);

        return types.Select(t => new DoctorPriceItem(t.Id, t.Name, t.NameAr, t.DefaultUnitPrice,
            map.TryGetValue(t.Id, out var v) ? v : null)).ToList();
    }

    public async Task SetDoctorPricesAsync(int doctorId, IReadOnlyDictionary<int, decimal?> prices, CancellationToken ct = default)
    {
        foreach (var p in prices.Values)
            if (p is < 0 or > 10_000_000m) throw Fail("Price", "PriceRange");

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.DoctorPrices.Where(p => p.DoctorId == doctorId).ToListAsync(ct);

        foreach (var (typeId, price) in prices)
        {
            var row = existing.FirstOrDefault(e => e.RestorationTypeId == typeId);
            if (price is null) { if (row is not null) db.DoctorPrices.Remove(row); }
            else if (row is null) db.DoctorPrices.Add(DoctorPrice.Create(doctorId, typeId, price.Value));
            else row.SetPrice(price.Value);
        }

        db.AuditLogs.Add(AuditLog.Create("DoctorPricesUpdated", nameof(Doctor), doctorId.ToString()));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static ValidationException Fail(string property, string code)
        => new([new ValidationFailure(property, code) { ErrorCode = code }]);
}
