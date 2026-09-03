using Application.PfaRegistrations.Onboarding;
using Domain.PfaRegistrations;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// Regulile de deblocare (RL-01). Statusul pașilor se derivă, nu se stochează, deci întreaga
/// mașină de stare e testabilă fără bază de date — aici se prinde regresia, nu la integrare.
/// </summary>
public class OnboardingStepCatalogTests
{
    private const string Locked = "Locked";
    private const string InProgress = "InProgress";
    private const string Completed = "Completed";

    [Fact]
    public void BuildSteps_WithoutAnything_LeavesOnlyEligibilityOpen()
    {
        List<OnboardingStepDto> steps = Build(registration: null, eligibility: null);

        steps.Count.ShouldBe(6);
        steps[0].Status.ShouldBe(InProgress);
        steps[0].State.ShouldBe(OnboardingStepCatalog.States.Available);
        steps.Skip(1).ShouldAllBe(s => s.Status == Locked);
    }

    [Fact]
    public void BuildSteps_BlockedStep_ExplainsWhichStepComesFirst()
    {
        List<OnboardingStepDto> steps = Build(registration: null, eligibility: null);

        steps[1].BlockReason.ShouldNotBeNull().ShouldContain("Eligibilitate");
    }

    /// <summary>
    /// Regresia care a motivat RL-01: cu dosarul PFA validat se deschideau simultan „fiscal”,
    /// „arr” și „platforms”, fiindcă fiecare depindea direct de „pfa”. Acum lanțul e liniar.
    /// </summary>
    [Fact]
    public void BuildSteps_AfterPfaValidated_OpensOnlyFiscal()
    {
        List<OnboardingStepDto> steps = Build(
            Registration(),
            EligibleProfile(),
            OnboardingSectionStatus.Validated);

        steps[0].Status.ShouldBe(Completed);
        steps[1].Status.ShouldBe(Completed);
        steps[2].Status.ShouldBe(InProgress);   // fiscal
        steps[3].Status.ShouldBe(Locked);       // arr
        steps[4].Status.ShouldBe(Locked);       // platforms
        steps[5].Status.ShouldBe(Locked);       // vehicle
    }

    /// <summary>
    /// Invariantul supraviețuiește deblocării mai permisive, dar din alt motiv: un pas al cărui
    /// parte de user e gata devine <c>pending_admin</c>, deci nu mai e „al lui" nici după ce
    /// următorul s-a deschis. Șoferul are tot un singur loc în care are de lucrat.
    /// </summary>
    [Theory]
    [InlineData(OnboardingSectionStatus.InProgress)]
    [InlineData(OnboardingSectionStatus.AwaitingValidation)]
    [InlineData(OnboardingSectionStatus.Validated)]
    [InlineData(OnboardingSectionStatus.Rejected)]
    public void BuildSteps_AtMostOneStepIsActive(OnboardingSectionStatus pfaStatus)
    {
        List<OnboardingStepDto> steps = Build(Registration(), EligibleProfile(), pfaStatus);

        steps.Count(s => s.State is OnboardingStepCatalog.States.Available
                or OnboardingStepCatalog.States.InProgress
                or OnboardingStepCatalog.States.Rejected)
            .ShouldBeLessThanOrEqualTo(1);
    }

    /// <summary>
    /// Bugul raportat: „se blochează la certificatul de înregistrare". Dosarul depus e la admin
    /// zile întregi, iar poarta veche cerea „Completed" — deci șoferul stătea. Acum pasul următor
    /// se deschide pe dosarul depus, iar validarea merge în paralel.
    /// </summary>
    [Fact]
    public void BuildSteps_PfaAwaitingValidation_IsPendingAdminButUnlocksFiscal()
    {
        List<OnboardingStepDto> steps = Build(
            Registration(),
            EligibleProfile(),
            OnboardingSectionStatus.AwaitingValidation);

        steps[1].State.ShouldBe(OnboardingStepCatalog.States.PendingAdmin);
        steps[1].UserPartDone.ShouldBeTrue();
        steps[2].Status.ShouldNotBe(Locked);
        OnboardingStepCatalog.IsWritableByUser(steps, OnboardingStepKey.Fiscal).ShouldBeTrue();
    }

    /// <summary>
    /// Excepția pe care o păstrăm: împuternicirea din pachetul de semnături e chiar actul cu care
    /// se depune dosarul ARR, deci pasul 4 chiar nu poate începe fără ea.
    /// </summary>
    [Fact]
    public void BuildSteps_FiscalPendingAdmin_StillBlocksArr()
    {
        PfaRegistration registration = FiscalRegistration();
        registration.SignaturePacket = new OnboardingSignaturePacket
        {
            Id = Guid.NewGuid(),
            SubmittedForReviewAtUtc = DateTime.UtcNow,
        };

        List<OnboardingStepDto> steps = Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[2].UserPartDone.ShouldBeFalse();
        steps[3].Status.ShouldBe(Locked);
        // Mesajul spune că așteptarea e a noastră, nu îi cere lui să termine ce a terminat deja.
        steps[3].BlockReason.ShouldNotBeNull().ShouldContain("Așteptăm");
    }

    /// <summary>
    /// A doua jumătate a aceluiași bug: pasul 5 nu ducea la 6, fiindcă activarea conturilor în
    /// Uber/Bolt e a adminului. Credențialele complete sunt de-ajuns ca să meargă mai departe.
    /// </summary>
    [Fact]
    public void BuildSteps_PlatformsWithCredentials_UnlocksVehicle()
    {
        PfaRegistration registration = ReadyForPlatforms();
        registration.PlatformAccounts.Add(CompletePlatformAccount(PfaPlatformProvider.Bolt));

        List<OnboardingStepDto> steps = Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[4].UserPartDone.ShouldBeTrue();
        steps[5].Status.ShouldNotBe(Locked);
    }

    [Fact]
    public void BuildSteps_PlatformChosenButNotFilledIn_KeepsVehicleLocked()
    {
        PfaRegistration registration = ReadyForPlatforms();
        registration.PlatformAccounts.Add(new PfaPlatformAccount
        {
            Id = Guid.NewGuid(),
            Provider = PfaPlatformProvider.Bolt,
            Kind = PfaPlatformAccountKind.Driver,
            IsSelectedByUser = true,
        });

        List<OnboardingStepDto> steps = Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[4].UserPartDone.ShouldBeFalse();
        steps[5].Status.ShouldBe(Locked);
    }

    /// <summary>
    /// Deblocarea e mai permisivă, înrolarea NU: cât timp un pas așteaptă validarea adminului,
    /// dosarul nu se stampilează ca înrolat.
    /// </summary>
    [Fact]
    public void AllCompleted_StaysFalseWhileAStepAwaitsValidation()
    {
        List<OnboardingStepDto> steps = Build(
            Registration(),
            EligibleProfile(),
            OnboardingSectionStatus.AwaitingValidation);

        OnboardingStepCatalog.AllCompleted(steps).ShouldBeFalse();
    }

    /// <summary>
    /// `currentStep` e ținta de navigare din frontend. Dacă ar fi rămas „primul nefinalizat", un
    /// șofer ajuns la fiscal cu dosarul PFA încă la validare ar fi fost trimis înapoi la PFA.
    /// </summary>
    [Fact]
    public void CurrentStepKey_SkipsStepsWaitingOnAdmin()
    {
        OnboardingStepCatalog
            .CurrentStepKey(Build(Registration(), EligibleProfile(), OnboardingSectionStatus.AwaitingValidation))
            .ShouldBe("fiscal");
    }

    [Fact]
    public void BuildSteps_RejectedPfa_SurfacesRejectedState()
    {
        List<OnboardingStepDto> steps = Build(
            Registration(),
            EligibleProfile(),
            OnboardingSectionStatus.Rejected);

        steps[1].State.ShouldBe(OnboardingStepCatalog.States.Rejected);
    }

    [Fact]
    public void BuildSteps_StartedStep_IsInProgressNotAvailable()
    {
        PfaRegistration registration = Registration();
        registration.FiscalProfile = new PfaFiscalProfile { Id = Guid.NewGuid() };

        List<OnboardingStepDto> steps = Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[2].State.ShouldBe(OnboardingStepCatalog.States.InProgress);
    }

    [Fact]
    public void CurrentStepKey_IsTheFirstUnfinishedStep()
    {
        OnboardingStepCatalog
            .CurrentStepKey(Build(registration: null, eligibility: null))
            .ShouldBe("eligibility");

        OnboardingStepCatalog
            .CurrentStepKey(Build(Registration(), EligibleProfile(), OnboardingSectionStatus.Validated))
            .ShouldBe("fiscal");
    }

    [Fact]
    public void IsWritableByUser_AllowsOnlyTheActiveStep()
    {
        List<OnboardingStepDto> steps = Build(
            Registration(),
            EligibleProfile(),
            OnboardingSectionStatus.Validated);

        // Pasul curent.
        OnboardingStepCatalog.IsWritableByUser(steps, OnboardingStepKey.Fiscal).ShouldBeTrue();
        // Pași finalizați — read-only.
        OnboardingStepCatalog.IsWritableByUser(steps, OnboardingStepKey.Eligibility).ShouldBeFalse();
        OnboardingStepCatalog.IsWritableByUser(steps, OnboardingStepKey.Pfa).ShouldBeFalse();
        // Pași încă blocați — asta e cazul care întorcea 200 înainte de RL-01.
        OnboardingStepCatalog.IsWritableByUser(steps, OnboardingStepKey.Arr).ShouldBeFalse();
        OnboardingStepCatalog.IsWritableByUser(steps, OnboardingStepKey.Vehicle).ShouldBeFalse();
    }

    [Fact]
    public void IsWritableByUser_KeepsRejectedStepEditable()
    {
        List<OnboardingStepDto> steps = Build(
            Registration(),
            EligibleProfile(),
            OnboardingSectionStatus.Rejected);

        // Altfel o respingere ar fi o fundătură din care șoferul nu mai poate ieși.
        OnboardingStepCatalog.IsWritableByUser(steps, OnboardingStepKey.Pfa).ShouldBeTrue();
    }

    [Fact]
    public void WireKeyOf_MatchesTheKeysSentToTheClient()
    {
        List<OnboardingStepDto> steps = Build(registration: null, eligibility: null);

        foreach (OnboardingStepKey key in Enum.GetValues<OnboardingStepKey>())
        {
            steps.ShouldContain(s => s.Key == OnboardingStepCatalog.WireKeyOf(key));
        }
    }

    // --- RL-02: pasul fiscal se închide din admin ---

    [Fact]
    public void Fiscal_WithUserPartDoneButNoSignaturePacket_StaysOpen()
    {
        // Regula veche închidea pasul aici. Acum lipsește pachetul de semnături, deci nu.
        List<OnboardingStepDto> steps = Build(FiscalRegistration(), EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[2].Status.ShouldBe(InProgress);
        steps[3].Status.ShouldBe(Locked);
    }

    [Fact]
    public void Fiscal_AfterUserSubmits_IsPendingAdmin()
    {
        PfaRegistration registration = FiscalRegistration();
        registration.SignaturePacket = new OnboardingSignaturePacket
        {
            Id = Guid.NewGuid(),
            SubmittedForReviewAtUtc = DateTime.UtcNow,
        };

        List<OnboardingStepDto> steps = Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[2].State.ShouldBe(OnboardingStepCatalog.States.PendingAdmin);
        // Cerința RL-02: userul nu poate trece de pasul fiscal fără acțiune de admin.
        steps[3].Status.ShouldBe(Locked);
        OnboardingStepCatalog.IsWritableByUser(steps, OnboardingStepKey.Fiscal).ShouldBeFalse();
    }

    [Fact]
    public void Fiscal_AfterAdminCompletesPacket_UnlocksArr()
    {
        PfaRegistration registration = FiscalRegistration();
        registration.SignaturePacket = new OnboardingSignaturePacket
        {
            Id = Guid.NewGuid(),
            Status = SignaturePacketStatus.Completed,
            SignedAtUtc = DateTime.UtcNow,
        };

        List<OnboardingStepDto> steps = Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[2].Status.ShouldBe(Completed);
        steps[3].Status.ShouldBe(InProgress);
    }

    [Fact]
    public void Fiscal_WhenAdminRejects_ReturnsToUserAsRejected()
    {
        PfaRegistration registration = FiscalRegistration();
        registration.SignaturePacket = new OnboardingSignaturePacket
        {
            Id = Guid.NewGuid(),
            Status = SignaturePacketStatus.Rejected,
            RejectionReason = "Lipsește împuternicirea ANAF.",
        };

        List<OnboardingStepDto> steps = Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[2].State.ShouldBe(OnboardingStepCatalog.States.Rejected);
        OnboardingStepCatalog.IsWritableByUser(steps, OnboardingStepKey.Fiscal).ShouldBeTrue();
    }

    [Fact]
    public void Fiscal_IsOwnedByAdmin()
    {
        List<OnboardingStepDto> steps = Build(registration: null, eligibility: null);

        steps[2].OwnedBy.ShouldBe(OnboardingStepCatalog.Owners.Admin);
    }

    [Fact]
    public void FiscalUserPartComplete_NeedsVatBankAndOblio()
    {
        OnboardingStepCatalog.FiscalUserPartComplete(Registration()).ShouldBeFalse();
        OnboardingStepCatalog.FiscalUserPartComplete(FiscalRegistration()).ShouldBeTrue();

        PfaRegistration withoutOblio = FiscalRegistration();
        withoutOblio.OblioAccount = null;
        OnboardingStepCatalog.FiscalUserPartComplete(withoutOblio).ShouldBeFalse();
    }

    /// <summary>Dosar ajuns la pasul 5: fiscal închis de admin, dosarul ARR depus și autorizația emisă.</summary>
    private static PfaRegistration ReadyForPlatforms()
    {
        PfaRegistration registration = FiscalRegistration();
        registration.SignaturePacket = new OnboardingSignaturePacket
        {
            Id = Guid.NewGuid(),
            Status = SignaturePacketStatus.Completed,
            SignedAtUtc = DateTime.UtcNow,
        };
        registration.ArrAuthorizationRequest = new ArrAuthorizationRequest
        {
            Id = Guid.NewGuid(),
            Status = ArrAuthorizationStatus.Issued,
            SubmittedAtUtc = DateTime.UtcNow,
        };
        return registration;
    }

    /// <summary>Un cont de platformă cu tot ce cere <c>UserPartComplete</c>.</summary>
    private static PfaPlatformAccount CompletePlatformAccount(PfaPlatformProvider provider) => new()
    {
        Id = Guid.NewGuid(),
        Provider = provider,
        Kind = PfaPlatformAccountKind.Driver,
        IsSelectedByUser = true,
        Email = "flota@ridelance.ro",
        Phone = "+40712345678",
        PasswordProtected = "protejata",
        ExistingAccountAnswer = "None",
        DriverEmail = "sofer@example.com",
        DriverPhone = "+40712345679",
    };

    /// <summary>Dosar cu partea de fiscal a șoferului completă și contul bancar verificat.</summary>
    private static PfaRegistration FiscalRegistration()
    {
        PfaRegistration registration = Registration();
        registration.FiscalProfile = new PfaFiscalProfile { Id = Guid.NewGuid(), VatAnswer = VatAnswer.No };
        registration.BankAccountDeclaration = new PfaBankAccountDeclaration
        {
            Id = Guid.NewGuid(),
            Status = BankDeclarationStatus.Verified,
        };
        registration.OblioAccount = new PfaOblioAccount
        {
            Id = Guid.NewGuid(),
            AccountCreationConsent = true,
            DataProcessingConsent = true,
            EInvoiceConsent = true,
            AutoInvoicingConsent = true,
            RidelanceManagementConsent = true,
            TermsAcceptedConsent = true,
        };
        return registration;
    }

    private static List<OnboardingStepDto> Build(
        PfaRegistration? registration,
        OnboardingEligibilityProfile? eligibility,
        OnboardingSectionStatus pfaStatus = OnboardingSectionStatus.InProgress) =>
        OnboardingStepCatalog.BuildSteps(registration, pfaStatus, eligibility);

    private static OnboardingEligibilityProfile EligibleProfile() =>
        new() { Id = Guid.NewGuid(), Status = EligibilityStatus.Eligible };

    /// <summary>Dosar „Am PFA” gol: pasul PFA depinde doar de statusul secțiunii.</summary>
    private static PfaRegistration Registration() =>
        new() { Id = Guid.NewGuid(), RegistrationType = RegistrationType.AmPfa };
}
