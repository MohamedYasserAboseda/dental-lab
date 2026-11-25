using FluentValidation;

namespace DentalLab.Application.Doctors;

/// <summary>Error codes (not texts) are returned; the UI maps "Val_&lt;code&gt;" to localized messages.</summary>
public sealed class DoctorInputValidator : AbstractValidator<DoctorInput>
{
    public DoctorInputValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithErrorCode("NameRequired");
        RuleFor(x => x.FullName).MaximumLength(150).WithErrorCode("NameTooLong");

        RuleFor(x => x.Email!).EmailAddress().WithErrorCode("EmailInvalid")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Phone!).Matches(@"^[\d\s\+\-\(\)]{5,25}$").WithErrorCode("PhoneInvalid")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.OpeningBalance).InclusiveBetween(-1_000_000_000m, 1_000_000_000m)
            .WithErrorCode("BalanceRange");
    }
}
