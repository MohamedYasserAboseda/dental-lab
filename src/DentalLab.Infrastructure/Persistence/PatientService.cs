using DentalLab.Application;
using DentalLab.Application.Abstractions;
using DentalLab.Application.Doctors;
using DentalLab.Application.Patients;
using DentalLab.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace DentalLab.Infrastructure.Persistence;

public sealed class PatientService(
    IDbContextFactory<DentalLabDbContext> factory,
    IValidator<PatientInput> validator) : IPatientService
{
    public async Task<PagedResult<PatientListItem>> SearchAsync(PatientQuery q, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<Patient> query = db.Patients.AsNoTracking();

        if (q.DoctorId is > 0) query = query.Where(p => p.DoctorId == q.DoctorId);

        var term = q.Term?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var like = $"%{term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(p => EF.Functions.Like(p.FullName, like, "\\") ||
                                     (p.Code != null && EF.Functions.Like(p.Code, like, "\\")));
        }

        var total = await query.CountAsync(ct);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var rows = await query.OrderBy(p => p.FullName).Skip((page - 1) * size).Take(size)
            .Select(p => new
            {
                p.Id, p.FullName, p.DoctorId, DoctorName = p.Doctor!.FullName, p.Code,
                Cases = db.Cases.Count(c => c.PatientId == p.Id)
            }).ToListAsync(ct);

        return new PagedResult<PatientListItem>(
            rows.Select(r => new PatientListItem(r.Id, r.FullName, r.DoctorId, r.DoctorName, r.Code, r.Cases)).ToList(), total);
    }

    public async Task<PatientDetails?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Patients.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new PatientDetails(p.Id, p.FullName, p.DoctorId, p.Doctor!.FullName, p.Code, p.Notes))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> CreateAsync(PatientInput input, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(input, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Doctors.AnyAsync(d => d.Id == input.DoctorId, ct)) throw Fail("DoctorId", "DoctorRequired");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var patient = Patient.Create(input.FullName, input.DoctorId, input.Code, input.Notes);
        db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);
        db.AuditLogs.Add(AuditLog.Create("PatientCreated", nameof(Patient), patient.Id.ToString()));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return patient.Id;
    }

    public async Task UpdateAsync(int id, PatientInput input, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(input, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException($"Patient {id} not found.");
        if (!await db.Doctors.AnyAsync(d => d.Id == input.DoctorId, ct)) throw Fail("DoctorId", "DoctorRequired");
        if (patient.DoctorId != input.DoctorId && await db.Cases.AnyAsync(c => c.PatientId == id, ct))
            throw Fail("DoctorId", "PatientHasCases"); // keep case history consistent

        patient.Update(input.FullName, input.DoctorId, input.Code, input.Notes);
        db.AuditLogs.Add(AuditLog.Create("PatientUpdated", nameof(Patient), id.ToString()));
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LookupItem>> LookupByDoctorAsync(int doctorId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Patients.AsNoTracking().Where(p => p.DoctorId == doctorId)
            .OrderBy(p => p.FullName).Take(1000).Select(p => new LookupItem(p.Id, p.FullName)).ToListAsync(ct);
    }

    private static ValidationException Fail(string property, string code)
        => new([new ValidationFailure(property, code) { ErrorCode = code }]);
}
