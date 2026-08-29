using Application.PfaRegistrations.Onboarding;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// RL-03 — plata înființării vine ÎNAINTEA dosarului: e serviciul pentru care plătește omul, iar
/// dosarul e lucrul pe care îl începem după ce e achitat. Poarta e aceeași funcție folosită și de
/// starea de onboarding, și de crearea sesiunii Stripe, ca UI-ul și API-ul să nu poată ajunge la
/// concluzii diferite despre același dosar.
/// </summary>
public class InfiintarePaymentGateTests
{
    [Fact]
    public void CanPay_IsFalse_ForTheHasPfaBranch()
    {
        // Ramura „Am PFA” nu cumpără nimic: dosarul există deja.
        PfaRegistration registration = Registration(RegistrationType.AmPfa);

        OnboardingStateBuilder.CanPayInfiintare(registration, hasPaidInfiintare: false).ShouldBeFalse();
    }

    [Fact]
    public void CanPay_IsTrue_BeforeAnyDossierExists()
    {
        // Momentul plății: ramura e aleasă, dosarul încă nu e deschis. Asta e regula nouă —
        // înainte, poarta cerea un dosar semnat, deci se completa tot înainte de a se plăti ceva.
        PfaRegistration registration = Registration(RegistrationType.NuAmPfa);

        registration.CompanyFormationRequest.ShouldBeNull();
        OnboardingStateBuilder.CanPayInfiintare(registration, hasPaidInfiintare: false).ShouldBeTrue();
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

        OnboardingStateBuilder.CanPayInfiintare(registration, hasPaidInfiintare: false).ShouldBeTrue();
    }

    [Fact]
    public void CanPay_IsFalse_OnceAlreadyPaid()
    {
        PfaRegistration registration = Registration(RegistrationType.NuAmPfa);

        OnboardingStateBuilder.CanPayInfiintare(registration, hasPaidInfiintare: true).ShouldBeFalse();
    }

    [Fact]
    public void CanPay_IsFalse_WithoutARegistration()
    {
        OnboardingStateBuilder.CanPayInfiintare(registration: null, hasPaidInfiintare: false).ShouldBeFalse();
    }

    private static PfaRegistration Registration(RegistrationType type) =>
        new() { Id = Guid.NewGuid(), RegistrationType = type };

    private static CompanyFormationRequest Formation(CompanyFormationStatus status) =>
        new() { Id = Guid.NewGuid(), Status = status };
}
