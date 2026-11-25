namespace DentalLab.Domain.Services;

/// <summary>Numbers like INV-2026-000012 / RC-2026-000007. The sequence restarts every year.</summary>
public static class DocumentNumber
{
    public const string Invoice = "INV";
    public const string Receipt = "RC";

    public static string Prefix(string code, int year) => $"{code}-{year}-";
    public static string Format(string code, int year, int sequence) => $"{Prefix(code, year)}{sequence:000000}";

    public static int ParseSequence(string number)
    {
        var i = number.LastIndexOf('-');
        if (i < 0 || !int.TryParse(number[(i + 1)..], out var seq))
            throw new FormatException($"'{number}' is not a valid document number.");
        return seq;
    }
}
