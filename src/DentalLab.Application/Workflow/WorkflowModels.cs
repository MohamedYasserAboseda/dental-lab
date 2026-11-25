using DentalLab.Domain.Entities;

namespace DentalLab.Application.Workflow;

public sealed record BoardQuery(int? DoctorId, bool OverdueOnly);

public sealed record BoardCard(int CaseId, string CaseNumber, string PatientName, string DoctorName, string RestorationName,
    string? RestorationNameAr, IReadOnlyList<int> Teeth, DateTime ExpectedDeliveryDate, bool IsOverdue, CaseStatus Status);

public sealed record BoardColumn(CaseStatus Status, int TotalCount, IReadOnlyList<BoardCard> Cards);

public sealed record ProductionStats(int InProgress, int DueToday, int Overdue, int DeliveredLast30Days, double? AverageTurnaroundDays);

public sealed record BoardData(IReadOnlyList<BoardColumn> Columns, ProductionStats Stats);
