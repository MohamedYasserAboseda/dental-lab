namespace DentalLab.Domain.Services;

/// <summary>Case numbers look like C-2026-000123 (sequence restarts every year).</summary>
public static class CaseNumber
{
    public static string Prefix(int year) => $"C-{year}-";
    public static string Format(int year, int sequence) => $"{Prefix(year)}{sequence:000000}";

    public static int ParseSequence(string caseNumber)
    {
        var i = caseNumber.LastIndexOf('-');
        if (i < 0 || !int.TryParse(caseNumber[(i + 1)..], out var seq))
            throw new FormatException($"'{caseNumber}' is not a valid case number.");
        return seq;
    }
}
