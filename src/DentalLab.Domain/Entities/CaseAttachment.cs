namespace DentalLab.Domain.Entities;

public enum AttachmentKind { Photo = 1, Stl = 2, Ply = 3 }

/// <summary>One file attached to a case. The file itself lives outside the database; only its location (a generated name) is stored.</summary>
public class CaseAttachment
{
    public int Id { get; private set; }
    public int CaseId { get; private set; }
    /// <summary>The name the user knew the file by (shown in the UI). Never used as a path.</summary>
    public string FileName { get; private set; } = string.Empty;
    /// <summary>Generated name on disk (guid + extension).</summary>
    public string StoredFileName { get; private set; } = string.Empty;
    public AttachmentKind Kind { get; private set; }
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public DateTime AddedUtc { get; private set; }

    private CaseAttachment() { }

    public static CaseAttachment Create(int caseId, string fileName, string storedFileName, AttachmentKind kind,
        long sizeBytes, string sha256, DateTime utcNow)
    {
        if (caseId <= 0) throw new ArgumentException("A case is required.", nameof(caseId));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name is required.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(storedFileName)) throw new ArgumentException("Stored name is required.", nameof(storedFileName));
        if (sizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        return new CaseAttachment
        {
            CaseId = caseId, FileName = fileName.Trim(), StoredFileName = storedFileName, Kind = kind,
            SizeBytes = sizeBytes, Sha256 = sha256, AddedUtc = utcNow
        };
    }
}
