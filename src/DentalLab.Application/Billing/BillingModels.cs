using DentalLab.Domain.Entities;
using FluentValidation;

namespace DentalLab.Application.Billing;

public sealed record UnbilledCaseItem(int CaseId, string CaseNumber, string PatientName, string RestorationName,
    string? RestorationNameAr, DateTime ReceivedUtc, CaseStatus Status, int Quantity, decimal Total);

public sealed record InvoiceInput(int DoctorId, IReadOnlyList<int> CaseIds, decimal Discount, string? Description);
public sealed record InvoiceQuery(string? Term, int? DoctorId, InvoiceStatus? Status, int Page, int PageSize);

public sealed record InvoiceListItem(int Id, string Number, int DoctorId, string DoctorName, DateTime DateUtc,
    decimal Total, decimal Paid, decimal Remaining, InvoiceStatus Status);

public sealed record InvoiceLineItem(int CaseId, string CaseNumber, int Quantity, decimal UnitPrice, decimal Discount, decimal Amount);
public sealed record InvoicePaymentItem(int Id, string ReceiptNumber, DateTime Date, decimal Amount, PaymentMethod Method, bool IsReversed);

public sealed record InvoiceDetails(int Id, string Number, int DoctorId, string DoctorName, DateTime DateUtc, string? Description,
    decimal Subtotal, decimal Discount, decimal Total, decimal Paid, decimal Remaining, InvoiceStatus Status,
    DateTime? VoidedUtc, string? VoidReason, IReadOnlyList<InvoiceLineItem> Lines, IReadOnlyList<InvoicePaymentItem> Payments);

public sealed record OpenInvoiceItem(int Id, string Number, decimal Remaining);

public sealed record PaymentInput(int DoctorId, int? InvoiceId, DateTime Date, decimal Amount, PaymentMethod Method, string? Notes);
public sealed record PaymentQuery(string? Term, int? DoctorId, int Page, int PageSize);
public sealed record PaymentListItem(int Id, string ReceiptNumber, int DoctorId, string DoctorName, DateTime Date,
    decimal Amount, PaymentMethod Method, string? InvoiceNumber, bool IsReversed, string? Notes);

public sealed record AdjustmentInput(int DoctorId, decimal Amount, string Description);

public sealed class InvoiceInputValidator : AbstractValidator<InvoiceInput>
{
    public InvoiceInputValidator()
    {
        RuleFor(x => x.DoctorId).GreaterThan(0).WithErrorCode("DoctorRequired");
        RuleFor(x => x.CaseIds).Must(c => c is { Count: > 0 }).WithErrorCode("CasesRequired");
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0m).WithErrorCode("DiscountInvalid");
        RuleFor(x => x.Description!).MaximumLength(500).WithErrorCode("NotesTooLong").When(x => x.Description is not null);
    }
}

public sealed class PaymentInputValidator : AbstractValidator<PaymentInput>
{
    public PaymentInputValidator()
    {
        RuleFor(x => x.DoctorId).GreaterThan(0).WithErrorCode("DoctorRequired");
        RuleFor(x => x.Amount).GreaterThan(0m).LessThanOrEqualTo(1_000_000_000m).WithErrorCode("AmountInvalid");
        RuleFor(x => x.Method).IsInEnum().WithErrorCode("MethodInvalid");
        RuleFor(x => x.Date).NotEqual(default(DateTime)).WithErrorCode("DateRequired");
        RuleFor(x => x.Notes!).MaximumLength(500).WithErrorCode("NotesTooLong").When(x => x.Notes is not null);
    }
}

public sealed class AdjustmentInputValidator : AbstractValidator<AdjustmentInput>
{
    public AdjustmentInputValidator()
    {
        RuleFor(x => x.DoctorId).GreaterThan(0).WithErrorCode("DoctorRequired");
        RuleFor(x => x.Amount).NotEqual(0m).WithErrorCode("AmountInvalid");
        RuleFor(x => x.Amount).InclusiveBetween(-1_000_000_000m, 1_000_000_000m).WithErrorCode("AmountInvalid");
        RuleFor(x => x.Description).NotEmpty().WithErrorCode("DescriptionRequired");
        RuleFor(x => x.Description).MaximumLength(500).WithErrorCode("NotesTooLong");
    }
}
