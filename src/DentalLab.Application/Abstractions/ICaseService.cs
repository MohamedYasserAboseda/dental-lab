using DentalLab.Application.Cases;
using DentalLab.Application.Doctors;
using DentalLab.Domain.Entities;

namespace DentalLab.Application.Abstractions;

public interface ICaseService
{
    Task<PagedResult<CaseListItem>> SearchAsync(CaseQuery query, CancellationToken ct = default);
    Task<CaseDetails?> GetAsync(int id, CancellationToken ct = default);
    /// <summary>Generates a unique case number and snapshots the unit price. Throws ValidationException (codes in Val_*).</summary>
    Task<int> CreateAsync(CaseInput input, CancellationToken ct = default);
    Task UpdateAsync(int id, CaseInput input, CancellationToken ct = default);
    /// <summary>Throws ValidationException with code InvalidTransition when the move is not allowed.</summary>
    Task ChangeStatusAsync(int id, CaseStatus to, string? note, CancellationToken ct = default);
}
