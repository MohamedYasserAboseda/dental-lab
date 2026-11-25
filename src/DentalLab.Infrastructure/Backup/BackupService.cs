using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DentalLab.Application;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Backup;
using DentalLab.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DentalLab.Infrastructure.Backup;

public sealed record BackupLocations(string Root, string DatabaseFile, string CaseFilesDirectory, string BackupsDirectory)
{
    public string PendingRestoreFile => Path.Combine(Root, "pending-restore.json");
    public static BackupLocations Default => new(AppPaths.Root, AppPaths.DatabaseFile, AppPaths.CaseFiles, AppPaths.Backups);
}

internal sealed record ManifestFile(string Path, long Size);
internal sealed record BackupManifest(int FormatVersion, DateTime CreatedUtc, string AppVersion, string Kind,
    string DatabaseSha256, long DatabaseBytes, List<ManifestFile> Files);
internal sealed record PendingRestore(string BackupPath, DateTime RequestedUtc);

public sealed class BackupService(BackupLocations loc, ISettingsService settings, ILogger<BackupService>? log = null) : IBackupService
{
    private const string DbEntry = "dentallab.db";
    private const string ManifestEntry = "manifest.json";
    private const string FilesPrefix = "Files/Cases/";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    // ------------------------------------------------------------------ create

    public async Task<BackupInfo> CreateBackupAsync(BackupKind kind, CancellationToken ct = default)
    {
        var compress = (await GetSettingsAsync(ct)).Compress;
        await _gate.WaitAsync(ct);
        try { return await Task.Run(() => CreateCore(kind, compress), ct); }
        finally { _gate.Release(); }
    }

    private BackupInfo CreateCore(BackupKind kind, bool compress)
    {
        if (!File.Exists(loc.DatabaseFile)) throw new FileNotFoundException("The database does not exist yet.", loc.DatabaseFile);
        Directory.CreateDirectory(loc.BackupsDirectory);

        var name = $"dentallab-backup-{DateTime.Now:yyyyMMdd-HHmmssfff}-{kind.ToString().ToLowerInvariant()}.zip";
        var finalPath = Path.Combine(loc.BackupsDirectory, name);
        var tempZip = finalPath + ".tmp";
        var snapshot = Path.Combine(loc.BackupsDirectory, $"snapshot-{Guid.NewGuid():N}.db");
        var level = compress ? CompressionLevel.Optimal : CompressionLevel.NoCompression;

        try
        {
            // Consistent snapshot of the live database (safe while the app is running).
            using (var src = new SqliteConnection($"Data Source={loc.DatabaseFile}"))
            using (var dst = new SqliteConnection($"Data Source={snapshot}"))
            {
                src.Open(); dst.Open();
                src.BackupDatabase(dst);
            }
            SqliteConnection.ClearAllPools();

            var files = new List<ManifestFile>();
            using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshot, DbEntry, level);

                if (Directory.Exists(loc.CaseFilesDirectory))
                    foreach (var file in Directory.EnumerateFiles(loc.CaseFilesDirectory, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(loc.CaseFilesDirectory, file).Replace('\\', '/');
                        zip.CreateEntryFromFile(file, FilesPrefix + rel, level);
                        files.Add(new ManifestFile(rel, new FileInfo(file).Length));
                    }

                var manifest = new BackupManifest(1, DateTime.UtcNow, typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "0",
                    kind.ToString(), Sha256(snapshot), new FileInfo(snapshot).Length, files);
                var entry = zip.CreateEntry(ManifestEntry);
                using var w = new StreamWriter(entry.Open());
                w.Write(JsonSerializer.Serialize(manifest, Json));
            }

            File.Move(tempZip, finalPath);
            log?.LogInformation("Backup created: {Path} ({Kind})", finalPath, kind);
            return ToInfo(new FileInfo(finalPath))!;
        }
        finally
        {
            TryDelete(snapshot);
            TryDelete(tempZip);
        }
    }

    // ------------------------------------------------------------------ list / verify

    public Task<IReadOnlyList<BackupInfo>> GetBackupsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<BackupInfo> list = !Directory.Exists(loc.BackupsDirectory)
            ? []
            : new DirectoryInfo(loc.BackupsDirectory).EnumerateFiles("dentallab-backup-*.zip")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)   // names start with a sortable timestamp
                .Select(ToInfo).Where(i => i is not null).Select(i => i!).ToList();
        return Task.FromResult(list);
    }

    private static BackupInfo? ToInfo(FileInfo f)
    {
        var token = Path.GetFileNameWithoutExtension(f.Name).Split('-').Last();
        return Enum.TryParse<BackupKind>(token, ignoreCase: true, out var kind)
            ? new BackupInfo(f.Name, f.FullName, f.LastWriteTimeUtc, f.Length, kind) : null;
    }

    public Task<BackupVerification> VerifyAsync(string backupPath, CancellationToken ct = default)
        => Task.Run(() => VerifyCore(backupPath), ct);

    internal static BackupVerification VerifyCore(string backupPath)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"dentallab-verify-{Guid.NewGuid():N}.db");
        try
        {
            ZipArchive zip;
            try { zip = ZipFile.OpenRead(backupPath); }
            catch (Exception) { return new(false, "ZipUnreadable"); }

            using (zip)
            {
                BackupManifest? manifest = null;
                var me = zip.GetEntry(ManifestEntry);
                if (me is not null)
                {
                    try { using var r = new StreamReader(me.Open()); manifest = JsonSerializer.Deserialize<BackupManifest>(r.ReadToEnd()); }
                    catch (Exception) { /* handled below */ }
                }
                if (manifest is null) return new(false, "ManifestMissing");

                var db = zip.GetEntry(DbEntry);
                if (db is null) return new(false, "DatabaseMissing");
                try { db.ExtractToFile(temp, true); } catch (Exception) { return new(false, "ZipUnreadable"); }

                if (!string.Equals(Sha256(temp), manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
                    return new(false, "ChecksumMismatch");
                if (!IntegrityOk(temp)) return new(false, "IntegrityFailed");

                foreach (var f in manifest.Files)
                {
                    var e = zip.GetEntry(FilesPrefix + f.Path);
                    if (e is null || e.Length != f.Size) return new(false, "FilesMissing");
                }
                return new(true, "Ok");
            }
        }
        finally { SqliteConnection.ClearAllPools(); TryDelete(temp); }
    }

    public Task<BackupVerification> CheckDatabaseIntegrityAsync(CancellationToken ct = default)
        => Task.Run(() => IntegrityOk(loc.DatabaseFile) ? new BackupVerification(true, "Ok") : new BackupVerification(false, "IntegrityFailed"), ct);

    private static bool IntegrityOk(string dbFile)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbFile};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            return string.Equals(cmd.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------------ restore (staged)

    public async Task ScheduleRestoreAsync(string backupPath, CancellationToken ct = default)
    {
        var v = await VerifyAsync(backupPath, ct);
        if (!v.IsValid) throw new InvalidOperationException("Backup is not valid: " + v.Code);

        await CreateBackupAsync(BackupKind.PreRestore, ct); // never replace data without a safety copy
        Directory.CreateDirectory(loc.Root);
        await File.WriteAllTextAsync(loc.PendingRestoreFile,
            JsonSerializer.Serialize(new PendingRestore(backupPath, DateTime.UtcNow), Json), ct);
        log?.LogWarning("Restore scheduled from {Path}", backupPath);
    }

    /// <summary>Call at start-up BEFORE the database is opened. Returns true when a restore was applied.</summary>
    public static bool ApplyPendingRestore(BackupLocations loc)
    {
        if (!File.Exists(loc.PendingRestoreFile)) return false;
        try
        {
            var pending = JsonSerializer.Deserialize<PendingRestore>(File.ReadAllText(loc.PendingRestoreFile))
                ?? throw new InvalidOperationException("Pending restore file is unreadable.");

            var v = VerifyCore(pending.BackupPath);
            if (!v.IsValid) throw new InvalidOperationException("Backup is not valid: " + v.Code);

            var tempDb = loc.DatabaseFile + ".restoring";
            var tempFiles = loc.CaseFilesDirectory + ".restoring";
            using (var zip = ZipFile.OpenRead(pending.BackupPath))
            {
                zip.GetEntry(DbEntry)!.ExtractToFile(tempDb, true);

                if (Directory.Exists(tempFiles)) Directory.Delete(tempFiles, true);
                Directory.CreateDirectory(tempFiles);
                var root = Path.GetFullPath(tempFiles) + Path.DirectorySeparatorChar;
                foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith(FilesPrefix, StringComparison.Ordinal) && e.Name.Length > 0))
                {
                    var target = Path.GetFullPath(Path.Combine(tempFiles, e.FullName[FilesPrefix.Length..]));
                    if (!target.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException("Unsafe path in backup.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target, true);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(loc.DatabaseFile)!);
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" }) TryDelete(loc.DatabaseFile + suffix);
            File.Move(tempDb, loc.DatabaseFile, overwrite: true);

            var old = loc.CaseFilesDirectory + ".old";
            if (Directory.Exists(old)) Directory.Delete(old, true);
            if (Directory.Exists(loc.CaseFilesDirectory)) Directory.Move(loc.CaseFilesDirectory, old);
            else Directory.CreateDirectory(Path.GetDirectoryName(loc.CaseFilesDirectory)!);
            Directory.Move(tempFiles, loc.CaseFilesDirectory);
            if (Directory.Exists(old)) Directory.Delete(old, true);

            File.Delete(loc.PendingRestoreFile);
            return true;
        }
        catch
        {
            TryDelete(loc.PendingRestoreFile); // never loop on a broken restore
            throw;
        }
    }

    // ------------------------------------------------------------------ scheduling + settings

    public async Task<BackupInfo?> RunScheduledBackupAsync(CancellationToken ct = default)
    {
        var s = await GetSettingsAsync(ct);
        if (!s.AutoEnabled) return null;

        var all = await GetBackupsAsync(ct);
        var last = all.Where(b => b.Kind == BackupKind.Auto).Select(b => (DateTime?)b.CreatedUtc).Max();
        if (last is not null && DateTime.UtcNow - last.Value < TimeSpan.FromHours(s.IntervalHours)) return null;

        var info = await CreateBackupAsync(BackupKind.Auto, ct);
        foreach (var old in (await GetBackupsAsync(ct)).Where(b => b.Kind == BackupKind.Auto).Skip(s.RetentionCount))
            TryDelete(old.FullPath); // retention only ever touches automatic backups
        return info;
    }

    public async Task<BackupSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var d = BackupSettings.Default;
        bool B(string? v, bool def) => bool.TryParse(v, out var r) ? r : def;
        int I(string? v, int def) => int.TryParse(v, out var r) && r > 0 ? r : def;
        return new BackupSettings(
            B(await settings.GetAsync(SettingKeys.AutoBackupEnabled, ct), d.AutoEnabled),
            I(await settings.GetAsync(SettingKeys.AutoBackupIntervalHours, ct), d.IntervalHours),
            I(await settings.GetAsync(SettingKeys.BackupRetentionCount, ct), d.RetentionCount),
            B(await settings.GetAsync(SettingKeys.BackupCompress, ct), d.Compress));
    }

    public async Task SaveSettingsAsync(BackupSettings s, CancellationToken ct = default)
    {
        await settings.SetAsync(SettingKeys.AutoBackupEnabled, s.AutoEnabled.ToString(), ct);
        await settings.SetAsync(SettingKeys.AutoBackupIntervalHours, Math.Clamp(s.IntervalHours, 1, 720).ToString(), ct);
        await settings.SetAsync(SettingKeys.BackupRetentionCount, Math.Clamp(s.RetentionCount, 1, 365).ToString(), ct);
        await settings.SetAsync(SettingKeys.BackupCompress, s.Compress.ToString(), ct);
    }

    // ------------------------------------------------------------------ helpers

    private static string Sha256(string file)
    {
        using var fs = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
