namespace DentalLab.Domain;

/// <summary>All money is decimal, rounded to 2 places (away from zero). Never double/float.</summary>
public static class Money
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
