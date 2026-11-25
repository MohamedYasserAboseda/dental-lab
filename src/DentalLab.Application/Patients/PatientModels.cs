using FluentValidation;

namespace DentalLab.Application.Patients;

public sealed record PatientInput(string FullName, int DoctorId, string? Code, string? Notes);
public sealed record PatientQuery(string? Term, int? DoctorId, int Page, int PageSize);
public sealed record PatientListItem(int Id, string Name, int DoctorId, string DoctorName, string? Code, int CaseCount);
public sealed record PatientDetails(int Id, string FullName, int DoctorId, string DoctorName, string? Code, string? Notes);

public sealed class PatientInputValidator : AbstractValidator<PatientInput>
{
    public PatientInputValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithErrorCode("NameRequired");
        RuleFor(x => x.FullName).MaximumLength(150).WithErrorCode("NameTooLong");
        RuleFor(x => x.DoctorId).GreaterThan(0).WithErrorCode("DoctorRequired");
        RuleFor(x => x.Code!).MaximumLength(30).WithErrorCode("CodeTooLong").When(x => x.Code is not null);
        RuleFor(x => x.Notes!).MaximumLength(1000).WithErrorCode("NotesTooLong").When(x => x.Notes is not null);
    }
}
