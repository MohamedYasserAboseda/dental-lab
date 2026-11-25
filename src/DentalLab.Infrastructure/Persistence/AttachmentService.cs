using System.Security.Cryptography;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Attachments;
using DentalLab.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace DentalLab.Infrastructure.Persistence;

public sealed class AttachmentService(
    IDbContextFactory<DentalLabDbContext> factory, string caseFilesDirectory, AttachmentLimits limits) : IAttachmentService
{
    public async Task<IReadOnlyList<AttachmentItem>> ListAsync(int caseId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.CaseAttachments.AsNoTracking().Where(a => a.CaseId == caseId)
            .OrderByDescending(a => a.AddedUtc).ThenByDescending(a => a.Id).ToListAsync(ct);
        return rows.Select(ToItem).ToList();
    }

    public async Task<AttachmentItem> AddAsync(int caseId, string sourcePath, CancellationToken ct = default)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw Fail("FileMissing");

        byte[] header;
        await using (var probe = File.OpenRead(sourcePath))
        {
            header = new byte[(int)Math.Min(512, info.Length)];
            var read = 0;
            while (read < header.Length)
            {
                var n = await probe.ReadAsync(header.AsMemory(read), ct);
                if (n == 0) break;
                read += n;
            }
        }

        var (kind, error) = AttachmentRules.Validate(info.Name, header, info.Length, limits);
        if (kind is null) throw Fail(error ?? "ContentMismatch");

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Cases.AnyAsync(c => c.Id == caseId, ct)) throw Fail("CaseNotFound");

        var dir = Path.Combine(caseFilesDirectory, caseId.ToString());
        Directory.CreateDirectory(dir);
        var stored = Guid.NewGuid().ToString("N") + info.Extension.ToLowerInvariant();
        var target = Path.Combine(dir, stored);

        string sha;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var src = File.OpenRead(sourcePath))
            await using (var dst = File.Create(target))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    hash.AppendData(buffer, 0, n);
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            sha = Convert.ToHexString(hash.GetHashAndReset());

            var entity = CaseAttachment.Create(caseId, Path.GetFileName(sourcePath), stored, kind.Value, info.Length, sha, DateTime.UtcNow);
            db.CaseAttachments.Add(entity);
            db.AuditLogs.Add(AuditLog.Create("AttachmentAdded", nameof(LabCase), caseId.ToString(), $"{entity.FileName} ({kind})"));
            await db.SaveChangesAsync(ct);
            return ToItem(entity);
        }
        catch
        {
            TryDelete(target); // never leave an orphan file behind
            throw;
        }
    }

    public async Task RemoveAsync(int attachmentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var a = await db.CaseAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId, ct)
            ?? throw new InvalidOperationException($"Attachment {attachmentId} not found.");

        var path = Resolve(a);
        db.CaseAttachments.Remove(a);
        db.AuditLogs.Add(AuditLog.Create("AttachmentRemoved", nameof(LabCase), a.CaseId.ToString(), a.FileName));
        await db.SaveChangesAsync(ct);
        if (path is not null) TryDelete(path);
    }

    public async Task<string?> GetFilePathAsync(int attachmentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var a = await db.CaseAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId, ct);
        var path = a is null ? null : Resolve(a);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>The path is always built from the generated name and must stay inside the attachments folder.</summary>
    private string? Resolve(CaseAttachment a)
    {
        var root = Path.GetFullPath(caseFilesDirectory) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(caseFilesDirectory, a.CaseId.ToString(), a.StoredFileName));
        return full.StartsWith(root, StringComparison.Ordinal) ? full : null;
    }

    private static AttachmentItem ToItem(CaseAttachment a) => new(a.Id, a.CaseId, a.FileName, a.Kind, a.SizeBytes, a.AddedUtc);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static ValidationException Fail(string code) => new([new ValidationFailure("File", code) { ErrorCode = code }]);
}
