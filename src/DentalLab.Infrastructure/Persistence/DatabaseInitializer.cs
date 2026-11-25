using DentalLab.Application.Abstractions;
using DentalLab.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DentalLab.Infrastructure.Persistence;

public sealed class DatabaseInitializer(
    IDbContextFactory<DentalLabDbContext> factory,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        AppPaths.EnsureCreated();
        await using var db = await factory.CreateDbContextAsync(ct);

        if (db.Database.GetMigrations().Any())
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0 && File.Exists(AppPaths.DatabaseFile))
            {
                BackupBeforeMigration();
            }
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            // No migrations generated yet (first dev run). setup.bat creates InitialCreate.
            logger.LogWarning("No EF migrations found. Falling back to EnsureCreated.");
            await db.Database.EnsureCreatedAsync(ct);
        }

        await CatalogSeeder.SeedAsync(db, ct);
        await VerifyIntegrityAsync(ct);
    }

    private void BackupBeforeMigration()
    {
        var target = Path.Combine(AppPaths.Backups, $"pre-migration-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        using var src = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}");
        using var dst = new SqliteConnection($"Data Source={target}");
        src.Open();
        dst.Open();
        src.BackupDatabase(dst);
        logger.LogInformation("Pre-migration backup written to {Path}", target);
    }

    private async Task VerifyIntegrityAsync(CancellationToken ct)
    {
        await using var conn = new SqliteConnection($"Data Source={AppPaths.DatabaseFile}");
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var result = (await cmd.ExecuteScalarAsync(ct))?.ToString();
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogCritical("Database integrity check failed: {Result}", result);
            throw new InvalidOperationException("Database integrity check failed: " + result);
        }
    }
}
