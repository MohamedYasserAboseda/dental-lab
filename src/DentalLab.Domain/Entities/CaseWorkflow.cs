namespace DentalLab.Domain.Entities;

public enum CaseStatus
{
    Received = 1,
    Designing = 2,
    Production = 3,
    Finishing = 4,
    QualityControl = 5,
    Ready = 6,
    Delivered = 7,
    Cancelled = 8
}

/// <summary>The only place that decides which status changes are legal.</summary>
public static class CaseWorkflow
{
    private static readonly Dictionary<CaseStatus, CaseStatus[]> Next = new()
    {
        [CaseStatus.Received]       = [CaseStatus.Designing, CaseStatus.Production, CaseStatus.Cancelled],
        [CaseStatus.Designing]      = [CaseStatus.Production, CaseStatus.Cancelled],
        [CaseStatus.Production]     = [CaseStatus.Finishing, CaseStatus.Cancelled],
        [CaseStatus.Finishing]      = [CaseStatus.QualityControl, CaseStatus.Cancelled],
        [CaseStatus.QualityControl] = [CaseStatus.Ready, CaseStatus.Production, CaseStatus.Cancelled], // back to production = rework
        [CaseStatus.Ready]          = [CaseStatus.Delivered, CaseStatus.QualityControl, CaseStatus.Cancelled],
        [CaseStatus.Delivered]      = [],
        [CaseStatus.Cancelled]      = [],
    };

    /// <summary>The visible production steps (Cancelled is a side exit, not a step).</summary>
    public static readonly IReadOnlyList<CaseStatus> Steps =
    [
        CaseStatus.Received, CaseStatus.Designing, CaseStatus.Production, CaseStatus.Finishing,
        CaseStatus.QualityControl, CaseStatus.Ready, CaseStatus.Delivered
    ];

    public static IReadOnlyList<CaseStatus> AllowedNext(CaseStatus from) => Next[from];
    public static bool CanTransition(CaseStatus from, CaseStatus to) => Next[from].Contains(to);
    public static bool IsFinal(CaseStatus s) => s is CaseStatus.Delivered or CaseStatus.Cancelled;
}
