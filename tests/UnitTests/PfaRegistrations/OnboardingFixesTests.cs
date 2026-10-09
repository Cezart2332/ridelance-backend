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
    /* Avansul e prima lună de PFA Full (299 lei) și vine dintr-o singură constantă. */

    [Fact]
    public void OnboardingAdvance_IsTheFirstMonthOfPfaFull()
    {
        Pricing.OnboardingAdvance.OnboardingAdvanceBani.ShouldBe(29_900);
        Pricing.OnboardingAdvance.OnboardingAdvanceBani.ShouldBe(Pricing.Plans.PfaFullMonthlyBani);
        Pricing.OnboardingAdvance.OnboardingAdvanceIsRefundable.ShouldBeFalse();
    }

    [Fact]
    public void StripeCatalog_ReadsTheAdvanceFromPricing()
    {
        StripeCatalog.OnboardingAdvance.UnitAmountBani
            .ShouldBe(Pricing.OnboardingAdvance.OnboardingAdvanceBani);

        // Un preț Stripe e imutabil: cheia trebuie să poarte suma, altfel se regăsește prețul
        // vechi și modificarea din `Pricing` n-are niciun efect.
        StripeCatalog.OnboardingAdvance.LookupKey.ShouldContain("299");
    }

    /* §8.1 — județul ARR se precompletează, nu se cere de la utilizator. */

    [Fact]
    public void PrimaryCounty_FallsBackToTheCountyReadFromTheIdCard()
    {
        // Ramura „Am PFA": nu există dosar de înființare, deci nici `Solicitant.Domiciliu`.
        // Singura sursă e buletinul citit prin OCR — exact cazul în care selectul rămânea gol.
        var registration = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            RegistrationType = RegistrationType.AmPfa,
        };

        OnboardingStateResponse state = OnboardingStateBuilder.Build(
            registration, hasPaidInfiintare: false, countyFromIdCard: "Cluj");

        state.PrimaryCounty.ShouldBe("Cluj");
    }

    [Fact]
    public void PrimaryCounty_PrefersTheRegisteredOfficeOverTheIdCard()
    {
        var registration = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            RegistrationType = RegistrationType.NuAmPfa,
            CompanyFormationRequest = new CompanyFormationRequest
            {
                Id = Guid.NewGuid(),
                OfficeAddress = new Adresa { Judet = "Ilfov" },
            },
        };

        OnboardingStateResponse state = OnboardingStateBuilder.Build(
            registration, hasPaidInfiintare: false, countyFromIdCard: "Cluj");

        state.PrimaryCounty.ShouldBe("Ilfov");
    }

    [Fact]
    public void PrimaryCounty_StaysEmptyWhenNothingIsKnown()
    {
        OnboardingStateResponse state = OnboardingStateBuilder.Build(
            new PfaRegistration { Id = Guid.NewGuid() }, hasPaidInfiintare: false);

        // Gol, nu ghicit: specul cere explicit să nu inventăm un județ.
        state.PrimaryCounty.ShouldBeNull();
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
