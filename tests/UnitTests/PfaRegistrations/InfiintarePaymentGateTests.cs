using Application.PfaRegistrations.Onboarding;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// RL-03 — plata înființării vine DUPĂ completare. Poarta e aceeași funcție folosită și de starea
/// de onboarding, și de crearea sesiunii Stripe, ca UI-ul și API-ul să nu poată ajunge la
/// concluzii diferite despre același dosar.
/// </summary>
public class InfiintarePaymentGateTests
{
    [Fact]
    public void CanPay_IsFalse_ForTheHasPfaBranch()
    {
        // Ramura „Am PFA” nu cumpără nimic: dosarul există deja.
        PfaRegistration registration = Registration(RegistrationType.AmPfa);
        registration.CompanyFormationRequest = Formation(CompanyFormationStatus.Submitted);

        OnboardingStateBuilder.CanPayInfiintare(registration, hasPaidInfiintare: false).ShouldBeFalse();
    }

    [Fact]
    public void CanPay_IsFalse_BeforeTheDossierIsSigned()
    {
        // Exact regresia pe care o previne RL-03: plata înaintea datelor.
        PfaRegistration registration = Registration(RegistrationType.NuAmPfa);

        OnboardingStateBuilder.CanPayInfiintare(registration, hasPaidInfiintare: false).ShouldBeFalse();

        registration.CompanyFormationRequest = Formation(CompanyFormationStatus.Draft);
        OnboardingStateBuilder.CanPayInfiintare(registration, hasPaidInfiintare: false).ShouldBeFalse();
    }

    [Fact]
    public void CanPay_IsFalse_WhenWeAskedForMoreInformation()
    {
        PfaRegistration registration = Registration(RegistrationType.NuAmPfa);
        registration.CompanyFormationRequest = Formation(CompanyFormationStatus.InfoRequested);

        OnboardingStateBuilder.CanPayInfiintare(registration, hasPaidInfiintare: false).ShouldBeFalse();
    }

    [Fact]
    public void CanPay_IsTrue_OnceSigned()
    {
        PfaRegistration registration = Registration(RegistrationType.NuAmPfa);
        registration.CompanyFormationRequest = Formation(CompanyFormationStatus.Submitted);

        OnboardingStateBuilder.CanPayInfiintare(registration, hasPaidInfiintare: false).ShouldBeTrue();
    }

    [Fact]
    public void CanPay_IsFalse_OnceAlreadyPaid()
    {
        PfaRegistration registration = Registration(RegistrationType.NuAmPfa);
        registration.CompanyFormationRequest = Formation(CompanyFormationStatus.Submitted);

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
