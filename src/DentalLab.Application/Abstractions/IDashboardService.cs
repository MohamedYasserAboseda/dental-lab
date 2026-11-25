using DentalLab.Application.Dashboard;

namespace DentalLab.Application.Abstractions;

/// <summary>
/// Every figure comes from real records. Revenue = invoiced amounts (voided invoices excluded);
/// collected = payments received (reversed payments excluded).
/// </summary>
public interface IDashboardService
{
    Task<DashboardData> GetAsync(DashboardQuery query, CancellationToken ct = default);
}
