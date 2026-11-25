using DentalLab.Domain.Entities;

namespace DentalLab.Application.Attachments;

public sealed record AttachmentItem(int Id, int CaseId, string FileName, AttachmentKind Kind, long SizeBytes, DateTime AddedUtc);

public sealed record AttachmentLimits(long MaxPhotoBytes = 15L * 1024 * 1024, long MaxModelBytes = 200L * 1024 * 1024);

/// <summary>
/// Decides whether a file may be attached: by extension, size AND by what is really inside the file
/// (a renamed .exe never becomes a "photo"). Error codes map to Val_* texts.
/// </summary>
public static class AttachmentRules
{
    public static (AttachmentKind? Kind, string? Error) Validate(string fileName, byte[] header, long size, AttachmentLimits limits)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        AttachmentKind? kind = ext switch
        {
            ".jpg" or ".jpeg" or ".png" => AttachmentKind.Photo,
            ".stl" => AttachmentKind.Stl,
            ".ply" => AttachmentKind.Ply,
            _ => null
        };
        if (kind is null) return (null, "UnsupportedType");
        if (size <= 0) return (null, "FileEmpty");
        if (size > (kind == AttachmentKind.Photo ? limits.MaxPhotoBytes : limits.MaxModelBytes)) return (null, "FileTooLarge");

        var ok = kind switch
        {
            AttachmentKind.Photo => IsPng(header) || IsJpeg(header),
            AttachmentKind.Stl => IsStl(header, size),
            AttachmentKind.Ply => IsPly(header),
            _ => false
        };
        return ok ? (kind, null) : (null, "ContentMismatch");
    }

    private static bool IsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
    private static bool IsJpeg(byte[] b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    private static bool IsStl(byte[] b, long size)
    {
        // ASCII STL starts with "solid"; binary STL = 80-byte header + uint32 triangle count + 50 bytes per triangle.
        if (b.Length >= 5 && System.Text.Encoding.ASCII.GetString(b, 0, 5).Equals("solid", StringComparison.OrdinalIgnoreCase)) return true;
        return b.Length >= 84 && size == 84 + 50L * BitConverter.ToUInt32(b, 80);
    }

    private static bool IsPly(byte[] b)
        => b.Length >= 4 && b[0] == (byte)'p' && b[1] == (byte)'l' && b[2] == (byte)'y' && (b[3] == (byte)'\n' || b[3] == (byte)'\r');
}
