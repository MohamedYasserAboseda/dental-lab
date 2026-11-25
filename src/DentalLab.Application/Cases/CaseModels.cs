using DentalLab.Domain.Dental;
using DentalLab.Domain.Entities;
using FluentValidation;

namespace DentalLab.Application.Cases;

public sealed record CaseInput(
    int DoctorId, int PatientId, int RestorationTypeId, int? MaterialId, string? Shade, int Quantity,
    decimal UnitPrice, decimal Discount, DateTime ExpectedDeliveryDate, string? Notes, IReadOnlyList<int> Teeth);

public sealed record CaseQuery(string? Term, int? DoctorId, CaseStatus? Status, DateTime? FromUtc, DateTime? ToUtc,
    bool OverdueOnly, int Page, int PageSize)
{
    /// <summary>Optional: only the cases of one patient (the patient's history).</summary>
    public int? PatientId { get; init; }
}

public sealed record CaseListItem(
    int Id, string CaseNumber, string DoctorName, string PatientName, string RestorationName, string? RestorationNameAr,
    IReadOnlyList<int> Teeth, DateTime ReceivedUtc, DateTime ExpectedDeliveryDate, CaseStatus Status, decimal Total, bool IsOverdue);

public sealed record CaseHistoryItem(CaseStatus? From, CaseStatus To, DateTime ChangedUtc, string? Note);

public sealed record CaseDetails(
    int Id, string CaseNumber, int DoctorId, string DoctorName, int PatientId, string PatientName,
    int RestorationTypeId, string RestorationName, string? RestorationNameAr,
    int? MaterialId, string? MaterialName, string? MaterialNameAr, string? Shade, int Quantity,
    decimal UnitPrice, decimal Discount, decimal Total, DateTime ReceivedUtc, DateTime ExpectedDeliveryDate,
    DateTime? ActualDeliveryUtc, string? Notes, CaseStatus Status, bool IsOverdue, bool IsEditable,
    IReadOnlyList<int> Teeth, IReadOnlyList<CaseHistoryItem> History, bool IsInvoiced);

public sealed class CaseInputValidator : AbstractValidator<CaseInput>
{
    public CaseInputValidator()
    {
        RuleFor(x => x.DoctorId).GreaterThan(0).WithErrorCode("DoctorRequired");
        RuleFor(x => x.PatientId).GreaterThan(0).WithErrorCode("PatientRequired");
        RuleFor(x => x.RestorationTypeId).GreaterThan(0).WithErrorCode("TypeRequired");
        RuleFor(x => x.Quantity).InclusiveBetween(1, 999).WithErrorCode("QuantityRange");
        RuleFor(x => x.UnitPrice).InclusiveBetween(0m, 10_000_000m).WithErrorCode("PriceRange");
        RuleFor(x => x.Discount).Must((x, d) => d >= 0 && d <= x.Quantity * x.UnitPrice).WithErrorCode("DiscountInvalid");
        RuleFor(x => x.ExpectedDeliveryDate).NotEqual(default(DateTime)).WithErrorCode("DateRequired");
        RuleFor(x => x.Shade!).MaximumLength(30).WithErrorCode("ShadeTooLong").When(x => x.Shade is not null);
        RuleFor(x => x.Notes!).MaximumLength(2000).WithErrorCode("NotesTooLong").When(x => x.Notes is not null);
        RuleFor(x => x.Teeth).Must(t => t is null || t.All(Fdi.IsValid)).WithErrorCode("TeethInvalid");
    }
}
