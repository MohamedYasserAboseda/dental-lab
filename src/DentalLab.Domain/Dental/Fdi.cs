namespace DentalLab.Domain.Dental;

public enum Dentition { Permanent, Primary }
public enum ToothType { Incisor, Canine, Premolar, Molar }
/// <summary>FDI quadrants. Primary teeth (51-85) map onto the same four quadrants.</summary>
public enum Quadrant { UpperRight = 1, UpperLeft = 2, LowerLeft = 3, LowerRight = 4 }

public sealed record ToothInfo(int Number, Dentition Dentition, Quadrant Quadrant, int Position, ToothType Type, string NameKey)
{
    public bool IsUpper => Quadrant is Quadrant.UpperRight or Quadrant.UpperLeft;
}

/// <summary>FDI two-digit tooth numbering. Permanent: 11-18, 21-28, 31-38, 41-48. Primary: 51-55, 61-65, 71-75, 81-85.</summary>
public static class Fdi
{
    private static readonly string[] PermanentNames =
        ["CentralIncisor", "LateralIncisor", "Canine", "FirstPremolar", "SecondPremolar", "FirstMolar", "SecondMolar", "ThirdMolar"];
    private static readonly string[] PrimaryNames =
        ["CentralIncisor", "LateralIncisor", "Canine", "FirstMolar", "SecondMolar"];

    public static bool IsValid(int number) => TryGet(number) is not null;

    public static ToothInfo? TryGet(int number)
    {
        var q = number / 10;
        var p = number % 10;
        if (q is >= 1 and <= 4 && p is >= 1 and <= 8)
        {
            var type = p <= 2 ? ToothType.Incisor : p == 3 ? ToothType.Canine : p <= 5 ? ToothType.Premolar : ToothType.Molar;
            return new ToothInfo(number, Dentition.Permanent, (Quadrant)q, p, type, PermanentNames[p - 1]);
        }
        if (q is >= 5 and <= 8 && p is >= 1 and <= 5)
        {
            var type = p <= 2 ? ToothType.Incisor : p == 3 ? ToothType.Canine : ToothType.Molar;
            return new ToothInfo(number, Dentition.Primary, (Quadrant)(q - 4), p, type, PrimaryNames[p - 1]);
        }
        return null;
    }

    public static ToothInfo Get(int number)
        => TryGet(number) ?? throw new ArgumentOutOfRangeException(nameof(number), $"{number} is not a valid FDI tooth number.");

    /// <summary>Teeth in the order they are drawn, left to right as the viewer sees the chart (patient's right on the left).</summary>
    public static IReadOnlyList<int> ArchOrder(Dentition dentition, bool upper)
    {
        if (dentition == Dentition.Permanent)
        {
            var (right, left) = upper ? (10, 20) : (40, 30);
            return Enumerable.Range(1, 8).Reverse().Select(i => right + i).Concat(Enumerable.Range(1, 8).Select(i => left + i)).ToList();
        }
        else
        {
            var (right, left) = upper ? (50, 60) : (80, 70);
            return Enumerable.Range(1, 5).Reverse().Select(i => right + i).Concat(Enumerable.Range(1, 5).Select(i => left + i)).ToList();
        }
    }

    public static IEnumerable<int> All(Dentition dentition)
        => ArchOrder(dentition, true).Concat(ArchOrder(dentition, false));

    /// <summary>Distinct and sorted. Throws if any number is not a valid FDI tooth.</summary>
    public static IReadOnlyList<int> Normalize(IEnumerable<int> teeth)
    {
        var list = teeth.Distinct().OrderBy(n => n).ToList();
        foreach (var n in list) Get(n);
        return list;
    }
}
