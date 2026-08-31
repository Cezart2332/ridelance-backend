using Application.Companies.Commands.UpdateCompanyPage;
using Domain.Companies;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Companies;

/// <summary>
/// Locul de preluare — adresa, pinul și indicația de la „Unde ne găsiți".
///
/// Coordonatele ajung pe o hartă publică, deci ce se salvează trebuie să fie ori un punct
/// întreg, ori niciunul. Jumătate de punct ar fi lăsat harta să decidă singură ce face.
/// </summary>
public sealed class CompanyPickupLocationTests
{
    private static CompanyProfile Profile() => new() { LegalName = "Tuki Go SRL" };

    [Fact]
    public void Pinul_Complet_Se_Salveaza()
    {
        CompanyProfile profile = Profile();

        Result result = UpdateCompanyPageCommandHandler.ApplyPickup(
            profile,
            new PickupLocationInput("Str. Lungă 4, București", 44.4268, 26.1025, "  Intrarea din spate  "));

        result.IsSuccess.ShouldBeTrue();
        profile.PickupAddress.ShouldBe("Str. Lungă 4, București");
        profile.PickupLatitude.ShouldBe(44.4268);
        profile.PickupLongitude.ShouldBe(26.1025);
        profile.PickupNote.ShouldBe("Intrarea din spate");
    }

    [Fact]
    public void Adresa_Fara_Pin_E_Valida()
    {
        // Cineva scrie „București, Sector 3" fără să deschidă harta. Secțiunea arată textul.
        CompanyProfile profile = Profile();

        Result result = UpdateCompanyPageCommandHandler.ApplyPickup(
            profile,
            new PickupLocationInput("București, Sector 3", null, null, null));

        result.IsSuccess.ShouldBeTrue();
        profile.PickupAddress.ShouldBe("București, Sector 3");
        profile.PickupLatitude.ShouldBeNull();
        profile.PickupLongitude.ShouldBeNull();
    }

    [Theory]
    [InlineData(44.4268, null)]
    [InlineData(null, 26.1025)]
    public void Jumatate_De_Pereche_Nu_Se_Salveaza(double? latitude, double? longitude)
    {
        CompanyProfile profile = Profile();
        profile.PickupLatitude = 1;
        profile.PickupLongitude = 1;

        UpdateCompanyPageCommandHandler.ApplyPickup(
            profile, new PickupLocationInput(null, latitude, longitude, null));

        profile.PickupLatitude.ShouldBeNull();
        profile.PickupLongitude.ShouldBeNull();
    }

    [Theory]
    [InlineData(91, 26)]
    [InlineData(-91, 26)]
    [InlineData(44, 181)]
    [InlineData(44, -181)]
    public void Coordonatele_Din_Afara_Lumii_Se_Refuza(double latitude, double longitude)
    {
        Result result = UpdateCompanyPageCommandHandler.ApplyPickup(
            Profile(), new PickupLocationInput(null, latitude, longitude, null));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyPage.InvalidPin");
    }

    [Fact]
    public void Golirea_Sterge_Si_Pinul_Salvat_Anterior()
    {
        CompanyProfile profile = Profile();
        profile.PickupAddress = "ceva";
        profile.PickupLatitude = 44;
        profile.PickupLongitude = 26;

        UpdateCompanyPageCommandHandler.ApplyPickup(profile, null);

        profile.PickupAddress.ShouldBeNull();
        profile.PickupLatitude.ShouldBeNull();
        profile.PickupLongitude.ShouldBeNull();
    }
}
