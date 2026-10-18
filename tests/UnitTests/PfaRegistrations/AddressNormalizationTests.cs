using Domain.PfaRegistrations.CompanyFormation;
using Infrastructure.Localities;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>Județul și localitatea citite din buletin, aduse la forma din formular și din SIRUTA.</summary>
public sealed class AddressNormalizationTests
{
    [Theory]
    [InlineData("CJ", "Cluj")]
    [InlineData("Jud.CJ", "Cluj")]
    [InlineData("jud. bv", "Brașov")]
    [InlineData("CLUJ", "Cluj")]
    [InlineData("Bistrita-Nasaud", "Bistrița-Năsăud")]
    [InlineData("Braşov", "Brașov")]
    [InlineData("B", "București")]
    [InlineData("Mun. București", "București")]
    [InlineData("Sector 3", "București")]
    [InlineData("SATU MARE", "Satu Mare")]
    public void County_IsRecognised(string raw, string expected) =>
        Judete.Canonical(raw).ShouldBe(expected);

    [Fact]
    public void UnknownCounty_StaysUnrecognised() =>
        Judete.Canonical("Atlantis").ShouldBeNull();

    [Fact]
    public void CountyCode_FromName() =>
        Judete.Code("Bistrița-Năsăud").ShouldBe("BN");

    [Theory]
    [InlineData("Mun. Cluj-Napoca", "Cluj-Napoca")]
    [InlineData("Com. Florești", "Florești")]
    [InlineData("Sat Săliștea", "Săliștea")]
    [InlineData("Mun.București Sec.3", "Sectorul 3")]
    [InlineData("Sectorul 6", "Sectorul 6")]
    [InlineData("Voluntari", "Voluntari")]
    [InlineData("Mun.Cluj-Napoca", "Cluj-Napoca")]
    [InlineData("Orăștie", "Orăștie")]
    public void Locality_IsTheSirutaName(string raw, string expected) =>
        LocalitatiPostalCodeLookup.LocalityQuery(raw).ShouldBe(expected);
}
