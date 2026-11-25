using DentalLab.Application;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Backup;
using DentalLab.Infrastructure.Backup;
using Microsoft.Data.Sqlite;
using System.IO.Compression;
using Xunit;

namespace DentalLab.Application.Tests;

/// <summary>Real SQLite files and real zip files in a temp folder.</summary>
public sealed class BackupServiceTests : IDisposable
{
    private sealed class MemorySettings : ISettingsService
    {
        public Dictionary<string, string> Values { get; } = [];
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken ct = default) { Values[key] = value; return Task.CompletedTask; }
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "dentallab-tests-" + Guid.NewGuid().ToString("N"));
    private readonly BackupLocations _loc;
    private readonly MemorySettings _settings = new();
    private readonly BackupService _svc;

    public BackupServiceTests()
    {
        _loc = new BackupLocations(_root, Path.Combine(_root, "Database", "dentallab.db"),
            Path.Combine(_root, "Files", "Cases"), Path.Combine(_root, "Backups"));
        Directory.CreateDirectory(Path.GetDirectoryName(_loc.DatabaseFile)!);
        Directory.CreateDirectory(_loc.CaseFilesDirectory);
        Exec("CREATE TABLE Notes (Text TEXT); INSERT INTO Notes VALUES ('original');");
        File.WriteAllText(Path.Combine(_loc.CaseFilesDirectory, "scan1.stl"), "stl-data-1");
        _svc = new BackupService(_loc, _settings);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_loc.DatabaseFile}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }

    private string Read()
    {
        using var c = new SqliteConnection($"Data Source={_loc.DatabaseFile}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT group_concat(Text, '|') FROM Notes";
        var v = cmd.ExecuteScalar()?.ToString() ?? "";
        SqliteConnection.ClearAllPools();
        return v;
    }

    [Fact]
    public async Task CreateBackup_ProducesAValidZip_WithDatabaseFilesAndManifest()
    {
        var info = await _svc.CreateBackupAsync(BackupKind.Manual);
        Assert.True(File.Exists(info.FullPath));
        Assert.Equal(BackupKind.Manual, info.Kind);

        using (var zip = ZipFile.OpenRead(info.FullPath))
        {
            Assert.NotNull(zip.GetEntry("dentallab.db"));
            Assert.NotNull(zip.GetEntry("manifest.json"));
            Assert.NotNull(zip.GetEntry("Files/Cases/scan1.stl"));
        }
        Assert.True((await _svc.VerifyAsync(info.FullPath)).IsValid);
        Assert.Empty(Directory.GetFiles(_loc.BackupsDirectory, "snapshot-*")); // no temp files left behind
    }

    [Fact]
    public async Task Verify_DetectsCorruption_MissingManifest_AndMissingFiles()
    {
        var good = await _svc.CreateBackupAsync(BackupKind.Manual);

        var garbage = Path.Combine(_loc.BackupsDirectory, "garbage.zip");
        File.WriteAllText(garbage, "not a zip");
        Assert.Equal("ZipUnreadable", (await _svc.VerifyAsync(garbage)).Code);

        var noManifest = Path.Combine(_loc.BackupsDirectory, "nomanifest.zip");
        File.Copy(good.FullPath, noManifest);
        using (var z = ZipFile.Open(noManifest, ZipArchiveMode.Update)) z.GetEntry("manifest.json")!.Delete();
        Assert.Equal("ManifestMissing", (await _svc.VerifyAsync(noManifest)).Code);

        var noFile = Path.Combine(_loc.BackupsDirectory, "nofile.zip");
        File.Copy(good.FullPath, noFile);
        using (var z = ZipFile.Open(noFile, ZipArchiveMode.Update)) z.GetEntry("Files/Cases/scan1.stl")!.Delete();
        Assert.Equal("FilesMissing", (await _svc.VerifyAsync(noFile)).Code);

        var tampered = Path.Combine(_loc.BackupsDirectory, "tampered.zip");
        File.Copy(good.FullPath, tampered);
        using (var z = ZipFile.Open(tampered, ZipArchiveMode.Update))
        {
            z.GetEntry("dentallab.db")!.Delete();
            using var w = new StreamWriter(z.CreateEntry("dentallab.db").Open());
            w.Write("this is not the original database");
        }
        Assert.Equal("ChecksumMismatch", (await _svc.VerifyAsync(tampered)).Code);
    }

    [Fact]
    public async Task Restore_IsStaged_TakesSafetyBackup_AndReplacesDatabaseAndFiles()
    {
        var backup = await _svc.CreateBackupAsync(BackupKind.Manual);

        Exec("INSERT INTO Notes VALUES ('added later');");
        File.WriteAllText(Path.Combine(_loc.CaseFilesDirectory, "extra.ply"), "later");
        Assert.Equal("original|added later", Read());

        await _svc.ScheduleRestoreAsync(backup.FullPath);
        Assert.Equal("original|added later", Read());                       // nothing changes until the next start
        Assert.True(File.Exists(_loc.PendingRestoreFile));
        Assert.Contains((await _svc.GetBackupsAsync()), b => b.Kind == BackupKind.PreRestore);

        Assert.True(BackupService.ApplyPendingRestore(_loc));
        Assert.Equal("original", Read());
        Assert.True(File.Exists(Path.Combine(_loc.CaseFilesDirectory, "scan1.stl")));
        Assert.False(File.Exists(Path.Combine(_loc.CaseFilesDirectory, "extra.ply")));
        Assert.False(File.Exists(_loc.PendingRestoreFile));
        Assert.False(BackupService.ApplyPendingRestore(_loc));              // nothing pending any more

        // the safety backup still holds the data as it was just before the restore
        var safety = (await _svc.GetBackupsAsync()).First(b => b.Kind == BackupKind.PreRestore);
        Assert.True((await _svc.VerifyAsync(safety.FullPath)).IsValid);
    }

    [Fact]
    public async Task ScheduleRestore_RejectsInvalidBackup_AndDoesNotTouchAnything()
    {
        var bad = Path.Combine(_loc.BackupsDirectory, "dentallab-backup-bad.zip");
        Directory.CreateDirectory(_loc.BackupsDirectory);
        File.WriteAllText(bad, "junk");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ScheduleRestoreAsync(bad));
        Assert.False(File.Exists(_loc.PendingRestoreFile));
        Assert.Equal("original", Read());
    }

    [Fact]
    public void ApplyPendingRestore_WithBrokenMarker_ClearsItInsteadOfLooping()
    {
        File.WriteAllText(_loc.PendingRestoreFile, "{ \"BackupPath\": \"" + Path.Combine(_root, "missing.zip").Replace("\\", "\\\\") + "\" }");
        Assert.ThrowsAny<Exception>(() => BackupService.ApplyPendingRestore(_loc));
        Assert.False(File.Exists(_loc.PendingRestoreFile));
        Assert.Equal("original", Read());
    }

    [Fact]
    public async Task ScheduledBackup_RespectsEnabledFlagAndInterval()
    {
        await _svc.SaveSettingsAsync(new BackupSettings(false, 24, 14, true));
        Assert.Null(await _svc.RunScheduledBackupAsync());

        await _svc.SaveSettingsAsync(new BackupSettings(true, 24, 14, true));
        var first = await _svc.RunScheduledBackupAsync();
        Assert.NotNull(first);
        Assert.Equal(BackupKind.Auto, first!.Kind);
        Assert.Null(await _svc.RunScheduledBackupAsync());                  // not due yet
    }

    [Fact]
    public async Task Retention_KeepsNewestAutoBackups_AndNeverDeletesManualOnes()
    {
        await _svc.SaveSettingsAsync(new BackupSettings(true, 1, 2, true));
        var manual = await _svc.CreateBackupAsync(BackupKind.Manual);
        for (var i = 0; i < 4; i++) await _svc.CreateBackupAsync(BackupKind.Auto);

        // age the automatic backups so the scheduler considers a new one due
        foreach (var auto in (await _svc.GetBackupsAsync()).Where(b => b.Kind == BackupKind.Auto))
            File.SetLastWriteTimeUtc(auto.FullPath, DateTime.UtcNow.AddHours(-3));
        Assert.NotNull(await _svc.RunScheduledBackupAsync());

        var left = await _svc.GetBackupsAsync();
        Assert.Equal(2, left.Count(b => b.Kind == BackupKind.Auto));
        Assert.Contains(left, b => b.FullPath == manual.FullPath);
    }

    [Fact]
    public async Task Settings_RoundTrip_AndFallBackToDefaults()
    {
        Assert.Equal(BackupSettings.Default, await _svc.GetSettingsAsync());
        await _svc.SaveSettingsAsync(new BackupSettings(false, 6, 30, false));
        Assert.Equal(new BackupSettings(false, 6, 30, false), await _svc.GetSettingsAsync());
        await _svc.SaveSettingsAsync(new BackupSettings(true, 99999, 0, true));
        var clamped = await _svc.GetSettingsAsync();
        Assert.InRange(clamped.IntervalHours, 1, 720);
        Assert.True(clamped.RetentionCount >= 1);
    }

    [Fact]
    public async Task UncompressedBackups_AreStillValid_AndIntegrityCheckWorks()
    {
        await _svc.SaveSettingsAsync(new BackupSettings(true, 24, 14, Compress: false));
        var info = await _svc.CreateBackupAsync(BackupKind.Manual);
        Assert.True((await _svc.VerifyAsync(info.FullPath)).IsValid);
        Assert.True((await _svc.CheckDatabaseIntegrityAsync()).IsValid);
    }
}
