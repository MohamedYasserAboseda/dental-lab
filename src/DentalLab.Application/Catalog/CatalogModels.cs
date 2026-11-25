using FluentValidation;

namespace DentalLab.Application.Catalog;

public sealed record CatalogItem(int Id, string Name, string? NameAr, decimal DefaultPrice, bool IsActive)
{
    public string Display(bool arabic) => arabic && !string.IsNullOrWhiteSpace(NameAr) ? NameAr! : Name;
}

public sealed record CatalogItemInput(string Name, string? NameAr, decimal DefaultPrice, bool IsActive);

public sealed record DoctorPriceItem(int RestorationTypeId, string Name, string? NameAr, decimal DefaultPrice, decimal? DoctorPrice)
{
    public string Display(bool arabic) => arabic && !string.IsNullOrWhiteSpace(NameAr) ? NameAr! : Name;
}

public sealed class CatalogItemInputValidator : AbstractValidator<CatalogItemInput>
{
    public CatalogItemInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithErrorCode("NameRequired");
        RuleFor(x => x.Name).MaximumLength(100).WithErrorCode("NameTooLong");
        RuleFor(x => x.NameAr!).MaximumLength(100).WithErrorCode("NameTooLong").When(x => x.NameAr is not null);
        RuleFor(x => x.DefaultPrice).InclusiveBetween(0m, 10_000_000m).WithErrorCode("PriceRange");
    }
}
