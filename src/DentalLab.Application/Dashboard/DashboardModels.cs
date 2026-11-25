using DentalLab.Domain.Entities;

namespace DentalLab.Application.Dashboard;

/// <summary>Local calendar dates. From is inclusive, ToExclusive is exclusive; null = unbounded.</summary>
public sealed record DashboardQuery(DateTime? FromLocal, DateTime? ToExclusiveLocal);

public sealed record MonthValue(int Year, int Month, decimal Value);
public sealed record MonthCount(int Year, int Month, int Count);
public sealed record StatusCount(CaseStatus Status, int Count);
public sealed record DoctorCount(string Name, int Count);
public sealed record RecentPaymentItem(string Receipt, string DoctorName, decimal Amount, DateTime Date, bool IsReversed);
public sealed record UpcomingDeliveryItem(int CaseId, string CaseNumber, string PatientName, string DoctorName, DateTime Expected, bool IsOverdue);
public sealed record RecentActivityItem(string CaseNumber, CaseStatus To, DateTime ChangedUtc);

public sealed record DashboardData(
    int TotalDoctors, int TotalCases, int ReceivedToday, int InProduction, int ReadyForDelivery, int Overdue,
    decimal BilledToday, decimal CollectedToday, decimal BilledThisMonth, decimal CollectedThisMonth,
    decimal BilledInRange, decimal CollectedInRange, decimal OutstandingBalances,
    IReadOnlyList<MonthValue> MonthlyBilled, IReadOnlyList<MonthValue> MonthlyCollected, IReadOnlyList<MonthCount> MonthlyCases,
    IReadOnlyList<StatusCount> StatusDistribution, IReadOnlyList<DoctorCount> TopDoctors,
    IReadOnlyList<RecentPaymentItem> RecentPayments, IReadOnlyList<UpcomingDeliveryItem> UpcomingDeliveries,
    IReadOnlyList<RecentActivityItem> RecentActivity);
