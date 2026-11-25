using DentalLab.Application.Doctors;
using DentalLab.Domain.Services;

namespace DentalLab.Application.Abstractions;

public interface IDoctorService
{
    Task<PagedResult<DoctorListItem>> SearchAsync(DoctorQuery query, CancellationToken ct = default);
    Task<DoctorDetails?> GetAsync(int id, CancellationToken ct = default);
    /// <summary>Throws FluentValidation.ValidationException on invalid input.</summary>
    Task<int> CreateAsync(DoctorInput input, CancellationToken ct = default);
    /// <summary>Opening balance in the input is ignored: history is never overwritten.</summary>
    Task UpdateAsync(int id, DoctorInput input, CancellationToken ct = default);
    Task SetActiveAsync(int id, bool isActive, CancellationToken ct = default);
    Task<IReadOnlyList<StatementLine>> GetStatementAsync(int id, CancellationToken ct = default);
    /// <summary>Name list for dropdowns.</summary>
    Task<IReadOnlyList<LookupItem>> LookupAsync(bool activeOnly, CancellationToken ct = default);
}
