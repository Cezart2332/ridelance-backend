using Application.PfaRegistrations.Onboarding;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// RL-03 — avansul vine ÎNAINTEA dosarului: e o lună de RIDElance Start plătită din start, iar
/// lucrul la dosar începe după ce e achitată. Poarta e aceeași funcție folosită și de starea de
/// onboarding, și de crearea sesiunii Stripe, ca UI-ul și API-ul să nu poată ajunge la concluzii
/// diferite despre același dosar.
/// </summary>
public class InfiintarePaymentGateTests
{
    /// <summary>
    /// Avansul e pe abonament, nu pe înființare, deci îl datorează și cine are deja PFA. Cât timp
    /// poarta cerea ramura „Nu am PFA", jumătate din clienți parcurgeau tot onboardingul fără să
    /// fi plătit nimic.
    /// </summary>
    [Theory]
    [InlineData(RegistrationType.AmPfa)]
    [InlineData(RegistrationType.NuAmPfa)]
    public void CanPay_IsTrue_OnBothBranches(RegistrationType type)
    {
        PfaRegistration registration = Registration(type);

        OnboardingStateBuilder.CanPayOnboardingAdvance(registration, hasPaidAdvance: false).ShouldBeTrue();
    }

    [Fact]
    public void CanPay_IsTrue_BeforeAnyDossierExists()
    {
        // Momentul plății: ramura e aleasă, dosarul încă nu e deschis. Asta e regula nouă —
        // înainte, poarta cerea un dosar semnat, deci se completa tot înainte de a se plăti ceva.
        PfaRegistration registration = Registration(RegistrationType.NuAmPfa);

        registration.CompanyFormationRequest.ShouldBeNull();
        OnboardingStateBuilder.CanPayOnboardingAdvance(registration, hasPaidAdvance: false).ShouldBeTrue();
    }

    [Theory]
    [InlineData(CompanyFormationStatus.Draft)]
    [InlineData(CompanyFormationStatus.InfoRequested)]
    [InlineData(CompanyFormationStatus.AwaitingPayment)]
    public void CanPay_StaysTrue_ForDossiersOpenedUnderTheOldRule(CompanyFormationStatus status)
    {
        // Dosare deschise înainte de schimbare, încă neplătite: ecranul de plată trebuie să le
        // rămână disponibil, altfel ar fi blocate fără nicio cale de a achita.
        PfaRegistration registration = Registration(RegistrationType.NuAmPfa);
        registration.CompanyFormationRequest = Formation(status);

        OnboardingStateBuilder.CanPayOnboardingAdvance(registration, hasPaidAdvance: false).ShouldBeTrue();
    }

    [Fact]
    public void CanPay_IsFalse_OnceAlreadyPaid()
    {
        PfaRegistration registration = Registration(RegistrationType.NuAmPfa);

        OnboardingStateBuilder.CanPayOnboardingAdvance(registration, hasPaidAdvance: true).ShouldBeFalse();
    }

    [Fact]
    public void CanPay_IsFalse_WithoutARegistration()
    {
        OnboardingStateBuilder.CanPayOnboardingAdvance(registration: null, hasPaidAdvance: false).ShouldBeFalse();
    }

    /// <summary>
    /// Avansul se întoarce integral la primul abonament, în forma promisă clientului: Solo două
    /// luni gratis, Start una, Pro prima lună mai ieftină cu fix valoarea avansului.
    /// </summary>
    [Theory]
    [InlineData("solo", 19_900L, 2, 0L)]
    [InlineData("start", 39_900L, 1, 0L)]
    [InlineData("pro", 39_900L, 1, 20_000L)]
    public void AdvanceCredit_ReturnsTheWholeAdvance(
        string plan,
        long expectedAmountOff,
        int expectedMonths,
        long expectedFirstInvoice)
    {
        Pricing.OnboardingAdvanceCredit.Spec spec =
            Pricing.OnboardingAdvanceCredit.For(plan).ShouldNotBeNull();

        spec.AmountOffBani.ShouldBe(expectedAmountOff);
        spec.Months.ShouldBe(expectedMonths);

        // Reducerea totală acoperă avansul, mai puțin restul care nu se poate împărți pe luni
        // întregi: 399 nu se împarte la 199, deci Solo iese cu un leu sub. Peste o lună de plan
        // ar însemna că dăm gratis mai mult decât s-a plătit.
        long credited = spec.AmountOffBani * spec.Months;
        credited.ShouldBeInRange(
            Pricing.RidelanceStart.OnboardingAdvanceBani - spec.AmountOffBani + 1,
            Pricing.RidelanceStart.OnboardingAdvanceBani);

        long monthly = plan switch
        {
            "solo" => Pricing.Plans.SoloMonthlyBani,
            "start" => Pricing.Plans.StartMonthlyBani,
            _ => Pricing.Plans.ProMonthlyBani,
        };
        Math.Max(0, monthly - spec.AmountOffBani).ShouldBe(expectedFirstInvoice);
    }

    [Fact]
    public void AdvanceCredit_DoesNotApplyToTheFleetPlan()
    {
        // Flota nu trece prin onboardingul PFA, deci n-a plătit niciun avans.
        Pricing.OnboardingAdvanceCredit.For("fleet").ShouldBeNull();
    }

    private static PfaRegistration Registration(RegistrationType type) =>
        new() { Id = Guid.NewGuid(), RegistrationType = type };

    private static CompanyFormationRequest Formation(CompanyFormationStatus status) =>
        new() { Id = Guid.NewGuid(), Status = status };
}
