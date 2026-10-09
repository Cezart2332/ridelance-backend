using Application.PfaRegistrations.Onboarding.ArrFleet;
using Domain.PfaRegistrations;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>Județul sediului social, scris oricum, duce la agenția ARR corectă — sau la niciuna.</summary>
public sealed class ArrCountyMatcherTests
{
    private static readonly ArrAccount[] Accounts =
    [
        Account("CJ", "Cluj"),
        Account("BN", "Bistrița-Năsăud"),
        Account("B", "București"),
        Account("AB", "Alba"),
    ];

    [Theory]
    [InlineData("Cluj", "CJ")]
    [InlineData("CLUJ", "CJ")]
    [InlineData("jud. Cluj", "CJ")]
    [InlineData("Județul Cluj", "CJ")]
    [InlineData("Bistrita-Nasaud", "BN")]
    [InlineData("BISTRIŢA-NĂSĂUD", "BN")]
    [InlineData("Municipiul București", "B")]
    [InlineData("Bucuresti Sector 3", "B")]
    [InlineData("CJ", "CJ")]
    public void Match_FindsTheAgency(string county, string expectedCode) =>
        ArrCountyMatcher.Match(county, Accounts)!.CountyCode.ShouldBe(expectedCode);

    [Theory]
    [InlineData("Atlantida")]
    [InlineData("")]
    public void Match_WithoutAKnownCounty_ProposesNoAccount(string county) =>
        ArrCountyMatcher.Match(county, Accounts).ShouldBeNull();

    [Theory]
    [InlineData("Str. Memorandumului nr. 28, Jud. Cluj, Mun. Cluj-Napoca", "CLUJ")]
    [InlineData("Județul Bistrița-Năsăud, Oraș Beclean, Str. Libertății 4", "BISTRITA-NASAUD")]
    [InlineData("Municipiul București, Sector 5, Str. Izvor 1", "București")]
    public void CountyInAddress_ReadsTheExplicitCounty(string address, string expected) =>
        ArrCountyMatcher.CountyInAddress(address).ShouldBe(expected);

    /// <summary>Un nume de stradă nu e un județ: „Str. Alba Iulia” nu trimite banii la Alba.</summary>
    [Fact]
    public void CountyInAddress_IgnoresCountyNamesInStreetNames() =>
        ArrCountyMatcher.CountyInAddress("Str. Alba Iulia nr. 3, Cluj-Napoca").ShouldBeNull();

    private static ArrAccount Account(string code, string name) => new()
    {
        Id = Guid.NewGuid(),
        CountyCode = code,
        CountyName = name,
        Treasury = $"Trezoreria {name}",
        FiscalCode = "1",
        Iban = "RO00TREZ000000000000000",
    };
}
