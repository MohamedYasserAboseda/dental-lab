using DentalLab.Application.Backup;

namespace DentalLab.Application.Abstractions;

/// <summary>
/// Full backups are .zip files holding a consistent snapshot of the database, the case attachments and a manifest
/// with checksums. Restoring is staged: the safety backup is taken and the restore is scheduled, then it is applied
/// at the next start-up before the database is opened (so no file is locked).
/// </summary>
public interface IBackupService
{
    Task<BackupInfo> CreateBackupAsync(BackupKind kind, CancellationToken ct = default);
    Task<IReadOnlyList<BackupInfo>> GetBackupsAsync(CancellationToken ct = default);
    Task<BackupVerification> VerifyAsync(string backupPath, CancellationToken ct = default);
    /// <summary>Verifies the backup, takes a PreRestore safety backup and schedules the restore for the next start.</summary>
    Task ScheduleRestoreAsync(string backupPath, CancellationToken ct = default);
    Task<BackupVerification> CheckDatabaseIntegrityAsync(CancellationToken ct = default);
    /// <summary>Creates an automatic backup if auto-backup is on and the last one is older than the interval; then applies retention.</summary>
    Task<BackupInfo?> RunScheduledBackupAsync(CancellationToken ct = default);
    Task<BackupSettings> GetSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(BackupSettings settings, CancellationToken ct = default);
}
