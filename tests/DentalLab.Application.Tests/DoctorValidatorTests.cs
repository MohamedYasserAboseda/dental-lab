using DentalLab.Application.Doctors;
using Xunit;

namespace DentalLab.Application.Tests;

public class DoctorValidatorTests
{
    private readonly DoctorInputValidator _v = new();
    private static DoctorInput Ok() => new("Dr Ahmed", null, "0100 123 4567", "a@b.com", null, null, 0, true);

    [Fact] public void ValidInput_Passes() => Assert.True(_v.Validate(Ok()).IsValid);

    [Fact]
    public void EmptyName_ReturnsNameRequiredCode()
    {
        var r = _v.Validate(Ok() with { FullName = " " });
        Assert.Contains(r.Errors, e => e.ErrorCode == "NameRequired");
    }

    [Fact]
    public void BadEmail_ReturnsEmailInvalidCode()
    {
        var r = _v.Validate(Ok() with { Email = "not-an-email" });
        Assert.Contains(r.Errors, e => e.ErrorCode == "EmailInvalid");
    }

    [Fact]
    public void BadPhone_ReturnsPhoneInvalidCode()
    {
        var r = _v.Validate(Ok() with { Phone = "abc" });
        Assert.Contains(r.Errors, e => e.ErrorCode == "PhoneInvalid");
    }

    [Fact]
    public void BlankOptionalFields_AreAllowed()
        => Assert.True(_v.Validate(Ok() with { Email = "", Phone = null }).IsValid);

    [Fact]
    public void HugeOpeningBalance_IsRejected()
    {
        var r = _v.Validate(Ok() with { OpeningBalance = 5_000_000_000m });
        Assert.Contains(r.Errors, e => e.ErrorCode == "BalanceRange");
    }
}
