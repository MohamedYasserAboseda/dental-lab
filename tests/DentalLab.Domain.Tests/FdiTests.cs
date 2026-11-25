using DentalLab.Domain.Dental;
using Xunit;

namespace DentalLab.Domain.Tests;

public class FdiTests
{
    [Fact]
    public void Permanent_Has32Teeth_Primary_Has20()
    {
        Assert.Equal(32, Fdi.All(Dentition.Permanent).Count());
        Assert.Equal(20, Fdi.All(Dentition.Primary).Count());
        Assert.All(Fdi.All(Dentition.Permanent), n => Assert.True(Fdi.IsValid(n)));
        Assert.All(Fdi.All(Dentition.Primary), n => Assert.True(Fdi.IsValid(n)));
    }

    [Theory]
    [InlineData(0)] [InlineData(10)] [InlineData(19)] [InlineData(29)] [InlineData(49)]
    [InlineData(56)] [InlineData(86)] [InlineData(90)] [InlineData(-11)]
    public void InvalidNumbers_AreRejected(int n) => Assert.False(Fdi.IsValid(n));

    [Theory]
    [InlineData(11, ToothType.Incisor, Quadrant.UpperRight, "CentralIncisor")]
    [InlineData(23, ToothType.Canine, Quadrant.UpperLeft, "Canine")]
    [InlineData(35, ToothType.Premolar, Quadrant.LowerLeft, "SecondPremolar")]
    [InlineData(48, ToothType.Molar, Quadrant.LowerRight, "ThirdMolar")]
    [InlineData(54, ToothType.Molar, Quadrant.UpperRight, "FirstMolar")]
    [InlineData(83, ToothType.Canine, Quadrant.LowerRight, "Canine")]
    public void ToothInfo_IsCorrect(int n, ToothType type, Quadrant q, string name)
    {
        var t = Fdi.Get(n);
        Assert.Equal(type, t.Type);
        Assert.Equal(q, t.Quadrant);
        Assert.Equal(name, t.NameKey);
    }

    [Fact]
    public void ArchOrder_IsLeftToRightAsViewed()
    {
        Assert.Equal(new[] { 18, 17, 16, 15, 14, 13, 12, 11, 21, 22, 23, 24, 25, 26, 27, 28 }, Fdi.ArchOrder(Dentition.Permanent, true));
        Assert.Equal(new[] { 48, 47, 46, 45, 44, 43, 42, 41, 31, 32, 33, 34, 35, 36, 37, 38 }, Fdi.ArchOrder(Dentition.Permanent, false));
        Assert.Equal(new[] { 55, 54, 53, 52, 51, 61, 62, 63, 64, 65 }, Fdi.ArchOrder(Dentition.Primary, true));
        Assert.Equal(new[] { 85, 84, 83, 82, 81, 71, 72, 73, 74, 75 }, Fdi.ArchOrder(Dentition.Primary, false));
    }

    [Fact]
    public void Normalize_DedupesAndSorts_AndRejectsInvalid()
    {
        Assert.Equal(new[] { 11, 12, 36 }, Fdi.Normalize(new[] { 36, 11, 12, 11 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fdi.Normalize(new[] { 11, 99 }));
    }
}
