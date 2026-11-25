namespace DentalLab.Application.Backup;

public enum BackupKind { Manual, Auto, PreRestore }

public sealed record BackupInfo(string FileName, string FullPath, DateTime CreatedUtc, long SizeBytes, BackupKind Kind);

/// <summary>Code is "Ok" or one of: ZipUnreadable, ManifestMissing, DatabaseMissing, ChecksumMismatch, IntegrityFailed, FilesMissing.</summary>
public sealed record BackupVerification(bool IsValid, string Code);

public sealed record BackupSettings(bool AutoEnabled, int IntervalHours, int RetentionCount, bool Compress)
{
    public static BackupSettings Default => new(true, 24, 14, true);
}
