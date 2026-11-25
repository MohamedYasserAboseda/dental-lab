using DentalLab.Application.Abstractions;
using DentalLab.Application.Workflow;
using DentalLab.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalLab.Infrastructure.Persistence;

public sealed class WorkflowService(IDbContextFactory<DentalLabDbContext> factory) : IWorkflowService
{
    private const int MaxCardsPerColumn = 30;
    private static readonly CaseStatus[] Stages =
        [CaseStatus.Received, CaseStatus.Designing, CaseStatus.Production, CaseStatus.Finishing, CaseStatus.QualityControl, CaseStatus.Ready];

    public async Task<BoardData> GetBoardAsync(BoardQuery q, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var today = DateTime.Today;

        IQueryable<LabCase> open = db.Cases.AsNoTracking().Where(c => Stages.Contains(c.Status));
        if (q.DoctorId is > 0) open = open.Where(c => c.DoctorId == q.DoctorId);
        if (q.OverdueOnly) open = open.Where(c => c.ExpectedDeliveryDate < today);

        var counts = await open.GroupBy(c => c.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);

        var columns = new List<BoardColumn>();
        foreach (var stage in Stages)
        {
            var rows = await open.Where(c => c.Status == stage)
                .OrderBy(c => c.ExpectedDeliveryDate).ThenBy(c => c.Id).Take(MaxCardsPerColumn)
                .Select(c => new
                {
                    c.Id, c.CaseNumber, Patient = c.Patient!.FullName, Doctor = c.Doctor!.FullName,
                    RName = c.RestorationType!.Name, RNameAr = c.RestorationType.NameAr, c.ExpectedDeliveryDate
                }).ToListAsync(ct);

            var ids = rows.Select(r => r.Id).ToList();
            var teeth = await db.CaseTeeth.AsNoTracking().Where(t => ids.Contains(t.CaseId))
                .Select(t => new { t.CaseId, t.ToothNumber }).ToListAsync(ct);
            var byCase = teeth.GroupBy(t => t.CaseId).ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(x => x.ToothNumber).OrderBy(x => x).ToList());

            var cards = rows.Select(r => new BoardCard(r.Id, r.CaseNumber, r.Patient, r.Doctor, r.RName, r.RNameAr,
                byCase.GetValueOrDefault(r.Id) ?? [], r.ExpectedDeliveryDate, r.ExpectedDeliveryDate.Date < today, stage)).ToList();

            columns.Add(new BoardColumn(stage, counts.FirstOrDefault(c => c.Status == stage)?.Count ?? 0, cards));
        }

        // ---- statistics
        var inProgress = counts.Sum(c => c.Count);
        var dueToday = await open.CountAsync(c => c.ExpectedDeliveryDate >= today && c.ExpectedDeliveryDate < today.AddDays(1), ct);
        var overdue = await open.CountAsync(c => c.ExpectedDeliveryDate < today, ct);

        IQueryable<LabCase> delivered = db.Cases.AsNoTracking().Where(c => c.Status == CaseStatus.Delivered && c.ActualDeliveryUtc != null);
        if (q.DoctorId is > 0) delivered = delivered.Where(c => c.DoctorId == q.DoctorId);

        var now = DateTime.UtcNow;
        var since30 = now.AddDays(-30);
        var since90 = now.AddDays(-90);
        var deliveredLast30 = await delivered.CountAsync(c => c.ActualDeliveryUtc >= since30, ct);

        var turnaround = await delivered.Where(c => c.ActualDeliveryUtc >= since90)
            .Select(c => new { c.ReceivedUtc, c.ActualDeliveryUtc }).ToListAsync(ct);
        double? average = turnaround.Count == 0 ? null
            : Math.Round(turnaround.Average(t => (t.ActualDeliveryUtc!.Value - t.ReceivedUtc).TotalDays), 1);

        return new BoardData(columns, new ProductionStats(inProgress, dueToday, overdue, deliveredLast30, average));
    }
}
