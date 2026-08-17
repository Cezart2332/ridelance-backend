using Application.PfaRegistrations.Onboarding;
using Domain.Documents;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// Regulile din specul de fix-uri care au regresat deja o dată. Fiecare test aici corespunde unui
/// punct din spec — dacă unul pică, un fix s-a pierdut la o refactorizare, nu „a apărut un bug nou".
/// </summary>
public sealed class OnboardingFixesTests
{
    /* §7 — avizul medical și cel psihologic sunt DOUĂ documente. */

    [Fact]
    public void ArrRequirements_TreatMedicalAndPsychologicalAsSeparateDocuments()
    {
        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements =
            OnboardingSectionCatalog.RequirementsFor(OnboardingSectionKey.AutorizatieTransport);

        requirements.ShouldContain(r => r.AcceptedCategories.Contains(DocumentCategory.AdeverintaMedicala));
        requirements.ShouldContain(r => r.AcceptedCategories.Contains(DocumentCategory.AvizPsihologic));

        // Și, mai important: nu în aceeași cerință. Altfel unul l-ar satisface pe celălalt.
        requirements
            .Count(r => r.AcceptedCategories.Contains(DocumentCategory.AdeverintaMedicala)
                || r.AcceptedCategories.Contains(DocumentCategory.AvizPsihologic))
            .ShouldBe(2);
    }

    [Fact]
    public void ArrRequirements_LabelEachAvizDistinctly()
    {
        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements =
            OnboardingSectionCatalog.RequirementsFor(OnboardingSectionKey.AutorizatieTransport);

        requirements.ShouldContain(r => r.Label == "Aviz medical");
        requirements.ShouldContain(r => r.Label == "Aviz psihologic");
    }

    /* §11.2 — leasingul cere contract ȘI acordul finanțatorului. */

    [Fact]
    public void LeasedVehicle_RequiresBothContractAndFinancierAgreement()
    {
        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements =
            OnboardingSectionCatalog.RequirementsForVehicle(VehicleOwnershipMode.Leased);

        requirements.ShouldContain(r => r.Label == "Contract de leasing");
        requirements.ShouldContain(r => r.Label == "Acord de leasing");
    }

    [Fact]
    public void OwnedVehicle_RequiresNoContract()
    {
        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements =
            OnboardingSectionCatalog.RequirementsForVehicle(VehicleOwnershipMode.Owned);

        requirements.ShouldNotContain(r => r.AcceptedCategories.Contains(DocumentCategory.ContractVehicul));
        requirements.ShouldNotContain(r => r.AcceptedCategories.Contains(DocumentCategory.AcordLeasing));
    }

    [Theory]
    [InlineData(VehicleOwnershipMode.Rented, "Contract de închiriere")]
    [InlineData(VehicleOwnershipMode.Comodat, "Contract de comodat")]
    public void ContractLabel_NamesTheOwnershipMode(VehicleOwnershipMode mode, string expected)
    {
        OnboardingSectionCatalog.RequirementsForVehicle(mode)
            .ShouldContain(r => r.Label == expected);
    }

    /* §3 — avansul e 399 lei și vine dintr-o singură constantă. */

    [Fact]
    public void OnboardingAdvance_Is399Lei()
    {
        Pricing.RidelanceStart.OnboardingAdvanceBani.ShouldBe(39_900);
        Pricing.RidelanceStart.OnboardingAdvanceIsRefundable.ShouldBeFalse();
    }

    [Fact]
    public void StripeCatalog_ReadsTheAdvanceFromPricing()
    {
        StripeCatalog.RidelanceStartAdvance.UnitAmountBani
            .ShouldBe(Pricing.RidelanceStart.OnboardingAdvanceBani);

        // Un preț Stripe e imutabil: cheia trebuie să poarte suma, altfel se regăsește prețul
        // vechi și modificarea din `Pricing` n-are niciun efect.
        StripeCatalog.RidelanceStartAdvance.LookupKey.ShouldContain("399");
    }

    /* §2 — sediul social nu se poate închide fără cod poștal valid. */

    [Fact]
    public void RegisteredOffice_IsIncompleteWithoutPostalCode()
    {
        CompanyFormationRequest request = OwnOfficeRequest();
        request.OfficeAddress.CodPostal = null;

        request.RegisteredOfficeComplete.ShouldBeFalse();
    }

    [Fact]
    public void RegisteredOffice_IsIncompleteWithAMalformedPostalCode()
    {
        CompanyFormationRequest request = OwnOfficeRequest();
        request.OfficeAddress.CodPostal = "4001";

        request.RegisteredOfficeComplete.ShouldBeFalse();
    }

    [Fact]
    public void RegisteredOffice_IsCompleteWithSixDigits()
    {
        OwnOfficeRequest().RegisteredOfficeComplete.ShouldBeTrue();
    }

    private static CompanyFormationRequest OwnOfficeRequest() => new()
    {
        Id = Guid.NewGuid(),
        OfficeType = RegisteredOfficeType.Own,
        IsOwner = true,
        AcknowledgedOwnershipDocs = true,
        AcknowledgedSubmitLater = true,
        OfficeAddress = new Adresa
        {
            Judet = "Cluj",
            Localitate = "Cluj-Napoca",
            Strada = "Strada Testelor",
            Numar = "1",
            CodPostal = "400001",
        },
    };
}
