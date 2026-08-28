using Application.Users.PhoneVerification;
using Shouldly;
using Xunit;

namespace UnitTests.Users;

/// <summary>
/// Furnizorul de SMS acceptă o singură formă a numărului. Oamenii scriu alte șapte.
/// </summary>
public sealed class RomanianPhoneNumberTests
{
    [Theory]
    [InlineData("0722123456")]
    [InlineData("0722 123 456")]
    [InlineData("+40722123456")]
    [InlineData("+40 722 123 456")]
    [InlineData("0040722123456")]
    [InlineData("40722123456")]
    [InlineData("0722.123.456")]
    public void Formele_in_care_se_scrie_un_mobil_ajung_la_aceeasi_forma(string raw)
    {
        RomanianPhoneNumber.ToInternational(raw).ShouldBe("+40722123456");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0212345678")]   // fix, nu mobil
    [InlineData("072212345")]    // o cifră lipsă
    [InlineData("07221234567")]  // o cifră în plus
    [InlineData("nu e un numar")]
    public void Ce_nu_e_mobil_romanesc_nu_trece(string? raw)
    {
        RomanianPhoneNumber.ToInternational(raw).ShouldBeNull();
    }
}
