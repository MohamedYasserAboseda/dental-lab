using DentalLab.Application.Abstractions;
using DentalLab.Application.Dashboard;
using DentalLab.Domain.Entities;
using DentalLab.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace DentalLab.Infrastructure.Persistence;

public sealed class DashboardService(IDbContextFactory<DentalLabDbContext> factory) : IDashboardService
{
    private static readonly CaseStatus[] InProductionStatuses =
        [CaseStatus.Designing, CaseStatus.Production, CaseStatus.Finishing, CaseStatus.QualityControl];

    public async Task<DashboardData> GetAsync(DashboardQuery q, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var sixMonthsStart = monthStart.AddMonths(-5);
        var todayUtc = today.ToUniversalTime();
        var tomorrowUtc = tomorrow.ToUniversalTime();
        var fromUtc = q.FromLocal?.ToUniversalTime();
        var toUtc = q.ToExclusiveLocal?.ToUniversalTime();

        // ---- counters
        var totalDoctors = await db.Doctors.CountAsync(d => d.IsActive, ct);
        var totalCases = await db.Cases.CountAsync(ct);
        var receivedToday = await db.Cases.CountAsync(c => c.ReceivedUtc >= todayUtc && c.ReceivedUtc < tomorrowUtc, ct);
        var inProduction = await db.Cases.CountAsync(c => InProductionStatuses.Contains(c.Status), ct);
        var ready = await db.Cases.CountAsync(c => c.Status == CaseStatus.Ready, ct);
        var overdue = await db.Cases.CountAsync(c => c.ExpectedDeliveryDate < today &&
                                                      c.Status != CaseStatus.Delivered && c.Status != CaseStatus.Cancelled, ct);

        // ---- money (decimals are summed in memory: SQLite cannot SUM decimals)
        var invoices = (await db.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Voided)
            .Select(i => new { i.DateUtc, i.Total }).ToListAsync(ct))
            .Select(i => new { Date = DateTime.SpecifyKind(i.DateUtc, DateTimeKind.Utc).ToLocalTime(), i.Total }).ToList();
        var payments = await db.Payments.AsNoTracking().Where(p => !p.IsReversed)
            .Select(p => new { p.Date, p.Amount }).ToListAsync(ct);

        decimal Sum<T>(IEnumerable<T> items, Func<T, DateTime> date, Func<T, decimal> amount, DateTime from, DateTime? to)
            => Domain.Money.Round(items.Where(x => date(x) >= from && (to is null || date(x) < to)).Sum(amount));

        bool InRange(DateTime d) => (q.FromLocal is null || d >= q.FromLocal) && (q.ToExclusiveLocal is null || d < q.ToExclusiveLocal);

        var billedInRange = Domain.Money.Round(invoices.Where(i => InRange(i.Date)).Sum(i => i.Total));
        var collectedInRange = Domain.Money.Round(payments.Where(p => InRange(p.Date)).Sum(p => p.Amount));

        var months = Enumerable.Range(0, 6).Select(i => sixMonthsStart.AddMonths(i)).ToList();
        var monthlyBilled = months.Select(m => new MonthValue(m.Year, m.Month,
            Sum(invoices, i => i.Date, i => i.Total, m, m.AddMonths(1)))).ToList();
        var monthlyCollected = months.Select(m => new MonthValue(m.Year, m.Month,
            Sum(payments, p => p.Date, p => p.Amount, m, m.AddMonths(1)))).ToList();

        var ledger = await db.LedgerEntries.AsNoTracking().Select(l => new { l.DoctorId, l.Amount }).ToListAsync(ct);
        var outstanding = Domain.Money.Round(ledger.GroupBy(l => l.DoctorId)
            .Select(g => LedgerCalculator.Balance(g.Select(x => x.Amount))).Where(b => b > 0).Sum());

        // ---- case charts
        var sixMonthsUtc = sixMonthsStart.ToUniversalTime();
        var received = await db.Cases.AsNoTracking().Where(c => c.ReceivedUtc >= sixMonthsUtc).Select(c => c.ReceivedUtc).ToListAsync(ct);
        var monthlyCases = months.Select(m => new MonthCount(m.Year, m.Month,
            received.Count(r => { var l = DateTime.SpecifyKind(r, DateTimeKind.Utc).ToLocalTime(); return l.Year == m.Year && l.Month == m.Month; }))).ToList();

        IQueryable<LabCase> rangeCases = db.Cases.AsNoTracking();
        if (fromUtc is not null) rangeCases = rangeCases.Where(c => c.ReceivedUtc >= fromUtc);
        if (toUtc is not null) rangeCases = rangeCases.Where(c => c.ReceivedUtc < toUtc);

        var statusRows = await rangeCases.GroupBy(c => c.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var status = Enum.GetValues<CaseStatus>()
            .Select(s => new StatusCount(s, statusRows.FirstOrDefault(r => r.Status == s)?.Count ?? 0)).ToList();

        var topRows = await rangeCases.GroupBy(c => c.DoctorId).Select(g => new { DoctorId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(5).ToListAsync(ct);
        var ids = topRows.Select(t => t.DoctorId).ToList();
        var names = await db.Doctors.AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.FullName, ct);
        var topDoctors = topRows.Select(t => new DoctorCount(names.GetValueOrDefault(t.DoctorId, "?"), t.Count)).ToList();

        // ---- lists
        var recentPayments = (await db.Payments.AsNoTracking().OrderByDescending(p => p.CreatedUtc).ThenByDescending(p => p.Id).Take(5)
            .Select(p => new { p.ReceiptNumber, DoctorName = p.Doctor!.FullName, p.Amount, p.Date, p.IsReversed }).ToListAsync(ct))
            .Select(p => new RecentPaymentItem(p.ReceiptNumber, p.DoctorName, p.Amount, p.Date, p.IsReversed)).ToList();

        var upcoming = (await db.Cases.AsNoTracking()
            .Where(c => c.Status != CaseStatus.Delivered && c.Status != CaseStatus.Cancelled)
            .OrderBy(c => c.ExpectedDeliveryDate).ThenBy(c => c.Id).Take(6)
            .Select(c => new { c.Id, c.CaseNumber, Patient = c.Patient!.FullName, Doctor = c.Doctor!.FullName, c.ExpectedDeliveryDate }).ToListAsync(ct))
            .Select(c => new UpcomingDeliveryItem(c.Id, c.CaseNumber, c.Patient, c.Doctor, c.ExpectedDeliveryDate, c.ExpectedDeliveryDate.Date < today)).ToList();

        var activity = (await db.CaseStatusHistory.AsNoTracking().OrderByDescending(h => h.ChangedUtc).ThenByDescending(h => h.Id).Take(8)
            .Select(h => new { h.ToStatus, h.ChangedUtc, CaseNumber = db.Cases.Where(c => c.Id == h.CaseId).Select(c => c.CaseNumber).FirstOrDefault() })
            .ToListAsync(ct))
            .Select(h => new RecentActivityItem(h.CaseNumber ?? "?", h.ToStatus, h.ChangedUtc)).ToList();

        return new DashboardData(totalDoctors, totalCases, receivedToday, inProduction, ready, overdue,
            Sum(invoices, i => i.Date, i => i.Total, today, tomorrow), Sum(payments, p => p.Date, p => p.Amount, today, tomorrow),
            Sum(invoices, i => i.Date, i => i.Total, monthStart, monthStart.AddMonths(1)),
            Sum(payments, p => p.Date, p => p.Amount, monthStart, monthStart.AddMonths(1)),
            billedInRange, collectedInRange, outstanding,
            monthlyBilled, monthlyCollected, monthlyCases, status, topDoctors, recentPayments, upcoming, activity);
    }
}
