using DentalLab.Application.Doctors;
using DentalLab.Application.Patients;

namespace DentalLab.Application.Abstractions;

public interface IPatientService
{
    Task<PagedResult<PatientListItem>> SearchAsync(PatientQuery query, CancellationToken ct = default);
    Task<PatientDetails?> GetAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(PatientInput input, CancellationToken ct = default);
    Task UpdateAsync(int id, PatientInput input, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LookupByDoctorAsync(int doctorId, CancellationToken ct = default);
}
