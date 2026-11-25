namespace DentalLab.Application.Doctors;

public enum DoctorStatusFilter { All, Active, Inactive }

public sealed record DoctorQuery(string? Term, DoctorStatusFilter Status, int Page, int PageSize);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

public sealed record DoctorInput(
    string FullName, string? ClinicName, string? Phone, string? Email,
    string? Address, string? Notes, decimal OpeningBalance, bool IsActive);

public sealed record DoctorListItem(
    int Id, string Code, string FullName, string? ClinicName, string? Phone, bool IsActive, decimal Balance);

public sealed record DoctorDetails(
    int Id, string Code, string FullName, string? ClinicName, string? Phone, string? Email,
    string? Address, string? Notes, DateTime RegisteredUtc, decimal OpeningBalance, bool IsActive, decimal Balance);
