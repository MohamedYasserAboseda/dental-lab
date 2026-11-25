using System.Globalization;
using System.Text;

namespace DentalLab.Desktop.Helpers;

public static class NumberParser
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy"];

    /// <summary>Converts Arabic-Indic / Persian digits and the Arabic decimal separator to ASCII.</summary>
    public static string NormalizeDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch is >= '\u0660' and <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
            else if (ch is >= '\u06F0' and <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
            else if (ch == '\u066B') sb.Append('.');
            else if (ch == '\u066C') continue;
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Empty text = 0. Comma and spaces are treated as thousands separators.</summary>
    public static bool TryParseMoney(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var s = NormalizeDigits(text.Trim()).Replace(",", "").Replace(" ", "");
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return int.TryParse(NormalizeDigits(text.Trim()), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseDate(string? text, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return DateTime.TryParseExact(NormalizeDigits(text.Trim()), DateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);
    }
}
