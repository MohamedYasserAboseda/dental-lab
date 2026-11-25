using DentalLab.Application.Attachments;

namespace DentalLab.Application.Abstractions;

public interface IAttachmentService
{
    Task<IReadOnlyList<AttachmentItem>> ListAsync(int caseId, CancellationToken ct = default);
    /// <summary>Validates the file, copies it into the app's own folder and records it. Throws ValidationException (Val_* codes).</summary>
    Task<AttachmentItem> AddAsync(int caseId, string sourcePath, CancellationToken ct = default);
    Task RemoveAsync(int attachmentId, CancellationToken ct = default);
    /// <summary>Full path of the stored file, or null when the record or the file no longer exists.</summary>
    Task<string?> GetFilePathAsync(int attachmentId, CancellationToken ct = default);
}
