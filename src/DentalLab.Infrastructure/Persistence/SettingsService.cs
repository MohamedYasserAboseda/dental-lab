using DentalLab.Application.Abstractions;
using DentalLab.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalLab.Infrastructure.Persistence;

public sealed class SettingsService(IDbContextFactory<DentalLabDbContext> factory) : ISettingsService
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Settings.AsNoTracking().Where(s => s.Key == key)
            .Select(s => s.Value).FirstOrDefaultAsync(ct);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.Settings.FindAsync([key], ct);
        if (existing is null) db.Settings.Add(new AppSetting { Key = key, Value = value });
        else existing.Value = value;
        await db.SaveChangesAsync(ct);
    }
}
