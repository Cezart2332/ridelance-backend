using Application.PfaRegistrations.Onboarding;
using Domain.Payments;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// RL-03 — avansul se cere între eligibilitate și pasul PFA: o lună de RIDElance Start plătită
/// mai devreme, înaintea oricărei alegeri. Poarta e aceeași funcție folosită și de starea de
/// onboarding, și de crearea sesiunii Stripe, ca UI-ul și API-ul să nu poată ajunge la concluzii
/// diferite despre același client.
/// </summary>
public class InfiintarePaymentGateTests
{
    /// <summary>
    /// Singura condiție e plata. Nici ramura, nici dosarul nu contează — și nu pot conta: plata
    /// vine ÎNAINTEA întrebării „ai deja PFA?", deci înaintea dosarului. Cât timp poarta cerea
    /// ramura „Nu am PFA" și un dosar deschis, jumătate din clienți parcurgeau tot onboardingul
    /// fără să fi plătit nimic, iar ceilalți plăteau abia după ce alegeau.
    /// </summary>
    [Fact]
    public void CanPay_DependsOnlyOnWhetherTheAdvanceIsPaid()
    {
        OnboardingStateBuilder.CanPayOnboardingAdvance(hasPaidAdvance: false).ShouldBeTrue();
        OnboardingStateBuilder.CanPayOnboardingAdvance(hasPaidAdvance: true).ShouldBeFalse();
    }

    /// <summary>
    /// Avansul se întoarce integral la primul abonament, în forma promisă clientului: PFA Full o
    /// lună gratis, PFAlone două.
    /// </summary>
    [Theory]
    [InlineData("pfalone", 13_900L, 2, 0L)]
    [InlineData("pfa-full", 29_900L, 1, 0L)]
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
        // întregi: 299 nu se împarte la 139, deci PFAlone iese cu 21 de lei sub. Peste o lună de
        // plan ar însemna că dăm gratis mai mult decât s-a plătit.
        long credited = spec.AmountOffBani * spec.Months;
        credited.ShouldBeInRange(
            Pricing.OnboardingAdvance.OnboardingAdvanceBani - spec.AmountOffBani + 1,
            Pricing.OnboardingAdvance.OnboardingAdvanceBani);

        long monthly = plan == "pfalone" ? Pricing.Plans.PfaAloneMonthlyBani : Pricing.Plans.PfaFullMonthlyBani;
        Math.Max(0, monthly - spec.AmountOffBani).ShouldBe(expectedFirstInvoice);
    }

    [Fact]
    public void AdvanceCredit_DoesNotApplyToTheFleetPlan()
    {
        // Flota nu trece prin onboardingul PFA, deci n-a plătit niciun avans.
        Pricing.OnboardingAdvanceCredit.For("fleet").ShouldBeNull();
    }

    /// <summary>
    /// Descrierea cu care se scrie plata e cea pe care o caută <c>InfiintarePaymentCheck</c>.
    /// Rândul se naște fără dosar — plata precedă alegerea — deci dacă cele două texte ar
    /// diverge, clientul ar fi pus să plătească a doua oară exact între cele două ecrane.
    /// </summary>
    [Fact]
    public void AdvanceDescription_IsAConstantSharedByWriterAndReader()
    {
        Pricing.OnboardingAdvance.OnboardingAdvanceDescription.ShouldNotBeNullOrWhiteSpace();
        Pricing.OnboardingAdvance.OnboardingAdvanceDescription
            .ShouldNotBe(Pricing.OnboardingAdvance.LegacyInfiintareDescription);
    }
}
