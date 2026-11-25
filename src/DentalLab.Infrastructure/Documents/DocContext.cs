using System.Globalization;
using System.Resources;
using DentalLab.Application.Documents;
using DentalLab.Shared;

namespace DentalLab.Infrastructure.Documents;

/// <summary>Document texts come from the same .resx files as the UI, but in the language chosen for the document.</summary>
internal sealed class DocText(AppLanguage language)
{
    private static readonly ResourceManager Rm = new("DentalLab.Shared.Resources.Strings", typeof(AppPaths).Assembly);
    private readonly CultureInfo _culture = new(language == AppLanguage.Arabic ? "ar" : "en");
    public string this[string key] => Rm.GetString(key, _culture) ?? key;
}

internal sealed class DocContext
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public required PrintFormat Format { get; init; }
    public required AppLanguage Language { get; init; }
    public required LabProfile Lab { get; init; }
    public byte[]? Logo { get; init; }
    public required DocText T { get; init; }

    public bool Rtl => Language == AppLanguage.Arabic;
    public bool Compact => Format == PrintFormat.Thermal80;
    public float BaseSize => Format switch { PrintFormat.A4 => 10f, PrintFormat.A5 => 9f, _ => 8f };
    public string LabName => string.IsNullOrWhiteSpace(Lab.Name) ? T["Doc_DefaultLabName"] : Lab.Name;

    public string Number(decimal v) => v.ToString("N2", Inv);
    public string Money(decimal v) => string.IsNullOrWhiteSpace(Lab.Currency) ? Number(v) : $"{Number(v)} {Lab.Currency}";
    public string Date(DateTime d) => d.ToString("yyyy-MM-dd", Inv);
    public string DateTimeText(DateTime d) => d.ToString("yyyy-MM-dd HH:mm", Inv);
}
