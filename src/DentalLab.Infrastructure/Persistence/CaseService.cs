using DentalLab.Application.Abstractions;
using DentalLab.Application.Cases;
using DentalLab.Application.Doctors;
using DentalLab.Domain.Entities;
using DentalLab.Domain.Services;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace DentalLab.Infrastructure.Persistence;

public sealed class CaseService(
    IDbContextFactory<DentalLabDbContext> factory,
    IValidator<CaseInput> validator) : ICaseService
{
    public async Task<PagedResult<CaseListItem>> SearchAsync(CaseQuery q, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var today = DateTime.Today;
        IQueryable<LabCase> query = db.Cases.AsNoTracking();

        if (q.DoctorId is > 0) query = query.Where(c => c.DoctorId == q.DoctorId);
        if (q.PatientId is > 0) query = query.Where(c => c.PatientId == q.PatientId);
        if (q.Status is not null) query = query.Where(c => c.Status == q.Status);
        if (q.FromUtc is not null) query = query.Where(c => c.ReceivedUtc >= q.FromUtc);
        if (q.ToUtc is not null) query = query.Where(c => c.ReceivedUtc < q.ToUtc);
        if (q.OverdueOnly)
            query = query.Where(c => c.ExpectedDeliveryDate < today &&
                                     c.Status != CaseStatus.Delivered && c.Status != CaseStatus.Cancelled);

        var term = q.Term?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var like = $"%{term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(c => EF.Functions.Like(c.CaseNumber, like, "\\") ||
                                     EF.Functions.Like(c.Patient!.FullName, like, "\\"));
        }

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var rows = await query.OrderByDescending(c => c.ReceivedUtc).ThenByDescending(c => c.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(c => new
            {
                c.Id, c.CaseNumber, DoctorName = c.Doctor!.FullName, PatientName = c.Patient!.FullName,
                RName = c.RestorationType!.Name, RNameAr = c.RestorationType.NameAr,
                c.ReceivedUtc, c.ExpectedDeliveryDate, c.Status, c.Total
            }).ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var teeth = await db.CaseTeeth.AsNoTracking().Where(t => ids.Contains(t.CaseId))
            .Select(t => new { t.CaseId, t.ToothNumber }).ToListAsync(ct);
        var teethByCase = teeth.GroupBy(t => t.CaseId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(x => x.ToothNumber).OrderBy(x => x).ToList());

        var items = rows.Select(r => new CaseListItem(r.Id, r.CaseNumber, r.DoctorName, r.PatientName, r.RName, r.RNameAr,
            teethByCase.GetValueOrDefault(r.Id) ?? [], r.ReceivedUtc, r.ExpectedDeliveryDate, r.Status, r.Total,
            !CaseWorkflow.IsFinal(r.Status) && r.ExpectedDeliveryDate.Date < today)).ToList();

        return new PagedResult<CaseListItem>(items, total);
    }

    public async Task<CaseDetails?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var c = await db.Cases.AsNoTracking().AsSplitQuery()
            .Include(x => x.Doctor).Include(x => x.Patient).Include(x => x.RestorationType).Include(x => x.Material)
            .Include(x => x.Teeth).Include(x => x.History)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;

        return new CaseDetails(c.Id, c.CaseNumber, c.DoctorId, c.Doctor!.FullName, c.PatientId, c.Patient!.FullName,
            c.RestorationTypeId, c.RestorationType!.Name, c.RestorationType.NameAr,
            c.MaterialId, c.Material?.Name, c.Material?.NameAr, c.Shade, c.Quantity, c.UnitPrice, c.Discount, c.Total,
            c.ReceivedUtc, c.ExpectedDeliveryDate, c.ActualDeliveryUtc, c.Notes, c.Status,
            c.IsOverdue(DateTime.Today), c.IsEditable,
            c.Teeth.Select(t => t.ToothNumber).OrderBy(n => n).ToList(),
            c.History.OrderBy(h => h.ChangedUtc).ThenBy(h => h.Id)
                .Select(h => new CaseHistoryItem(h.FromStatus, h.ToStatus, h.ChangedUtc, h.Note)).ToList(),
            c.IsInvoiced);
    }

    public async Task<int> CreateAsync(CaseInput input, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(input, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await EnsureReferencesAsync(db, input, ct);

        var now = DateTime.UtcNow;
        var year = DateTime.Now.Year;
        var prefix = CaseNumber.Prefix(year);
        var last = await db.Cases.Where(c => c.CaseNumber.StartsWith(prefix))
            .OrderByDescending(c => c.CaseNumber).Select(c => c.CaseNumber).FirstOrDefaultAsync(ct);
        var number = CaseNumber.Format(year, last is null ? 1 : CaseNumber.ParseSequence(last) + 1);

        var labCase = LabCase.Create(number, input.DoctorId, input.PatientId, input.RestorationTypeId, input.MaterialId,
            input.Shade, input.Quantity, input.UnitPrice, input.Discount, input.ExpectedDeliveryDate, input.Notes,
            input.Teeth, now);
        db.Cases.Add(labCase);
        await db.SaveChangesAsync(ct);

        db.AuditLogs.Add(AuditLog.Create("CaseCreated", nameof(LabCase), labCase.Id.ToString(),
            $"{number} total={labCase.Total}"));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return labCase.Id;
    }

    public async Task UpdateAsync(int id, CaseInput input, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(input, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var c = await db.Cases.Include(x => x.Teeth).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Case {id} not found.");
        if (!c.IsEditable) throw Fail("Status", "NotEditable");
        if (c.IsInvoiced) throw Fail("Status", "CaseInvoiced");
        await EnsureReferencesAsync(db, input, ct);

        c.UpdateDetails(input.DoctorId, input.PatientId, input.RestorationTypeId, input.MaterialId, input.Shade,
            input.Quantity, input.UnitPrice, input.Discount, input.ExpectedDeliveryDate, input.Notes, input.Teeth);
        db.AuditLogs.Add(AuditLog.Create("CaseUpdated", nameof(LabCase), id.ToString(), $"{c.CaseNumber} total={c.Total}"));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ChangeStatusAsync(int id, CaseStatus to, string? note, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var c = await db.Cases.Include(x => x.History).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException($"Case {id} not found.");
        var from = c.Status;
        if (!CaseWorkflow.CanTransition(from, to)) throw Fail("Status", "InvalidTransition");
        if (to == CaseStatus.Cancelled && c.IsInvoiced) throw Fail("Status", "CaseInvoiced");

        c.ChangeStatus(to, note, DateTime.UtcNow);
        db.AuditLogs.Add(AuditLog.Create("CaseStatusChanged", nameof(LabCase), id.ToString(), $"{from} -> {to}"));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static async Task EnsureReferencesAsync(DentalLabDbContext db, CaseInput input, CancellationToken ct)
    {
        if (!await db.Doctors.AnyAsync(d => d.Id == input.DoctorId, ct)) throw Fail("DoctorId", "DoctorRequired");

        var patientDoctor = await db.Patients.Where(p => p.Id == input.PatientId)
            .Select(p => (int?)p.DoctorId).FirstOrDefaultAsync(ct);
        if (patientDoctor is null) throw Fail("PatientId", "PatientRequired");
        if (patientDoctor != input.DoctorId) throw Fail("PatientId", "PatientMismatch");

        if (!await db.RestorationTypes.AnyAsync(r => r.Id == input.RestorationTypeId, ct)) throw Fail("RestorationTypeId", "TypeRequired");
        if (input.MaterialId is int m && !await db.Materials.AnyAsync(x => x.Id == m, ct)) throw Fail("MaterialId", "MaterialInvalid");
    }

    private static ValidationException Fail(string property, string code)
        => new([new ValidationFailure(property, code) { ErrorCode = code }]);
}
