using Application.PfaRegistrations.Onboarding;
using Domain.Documents;
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
    /// Pasul fiscal trimis la verificare nu mai ține pe loc ARR-ul: pachetul de semnături vine pe
    /// email cât timp șoferul completează mai departe, iar adminul validează în paralel.
    /// </summary>
    [Fact]
    public void BuildSteps_FiscalPendingAdmin_UnlocksArr()
    {
        PfaRegistration registration = FiscalRegistration();
        registration.SignaturePacket = new OnboardingSignaturePacket
        {
            Id = Guid.NewGuid(),
            SubmittedForReviewAtUtc = DateTime.UtcNow,
        };

        List<OnboardingStepDto> steps = Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[2].UserPartDone.ShouldBeTrue();
        steps[3].Status.ShouldNotBe(Locked);
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
        // Pasul rămâne închis doar din admin, dar nu mai e o barieră: ARR-ul se deschide, iar o
        // corectură pe fiscal cât se așteaptă pachetul e încă permisă.
        steps[3].Status.ShouldNotBe(Locked);
        OnboardingStepCatalog.IsWritableByUser(steps, OnboardingStepKey.Fiscal).ShouldBeTrue();
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

    /// <summary>
    /// Regresie: pasul 3 se închidea doar pentru contul confirmat prin Open Banking. Cine declara
    /// contul de mână rămânea blocat cu toate bifele puse și cu secțiunea validată de admin.
    /// </summary>
    [Fact]
    public void Fiscal_WithManuallyDeclaredBankAccount_StillClosesAfterAdminValidates()
    {
        PfaRegistration registration = FiscalRegistration();
        registration.BankAccountDeclaration!.Status.ShouldBe(BankDeclarationStatus.Pending);
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

    /// <summary>
    /// „Validează secțiunea" închide pasul chiar dacă partea clientului a rămas neterminată.
    ///
    /// Regresia raportată: adminul apăsa butonul, primea „pasul următor al clientului este
    /// deblocat", iar clientul rămânea exact unde era. Poarta cerea în plus răspunsul la TVA și
    /// consimțămintele Oblio, deci un dosar căruia îi lipsea oricare din ele nu se putea închide
    /// din admin — și nimeni nu vedea de ce. Verdictul e al adminului; pentru dosarele incomplete
    /// există butonul de respingere, cu motiv.
    /// </summary>
    [Fact]
    public void Fiscal_AdminValidation_ClosesTheStepEvenWithTheClientPartUnfinished()
    {
        PfaRegistration registration = FiscalRegistration();
        registration.FiscalProfile = null;
        registration.OblioAccount = null;
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
    public void FiscalUserPartComplete_NeedsVatAndOblio_ButNotABankAccount()
    {
        OnboardingStepCatalog.FiscalUserPartComplete(Registration()).ShouldBeFalse();
        OnboardingStepCatalog.FiscalUserPartComplete(FiscalRegistration()).ShouldBeTrue();

        PfaRegistration withoutOblio = FiscalRegistration();
        withoutOblio.OblioAccount = null;
        OnboardingStepCatalog.FiscalUserPartComplete(withoutOblio).ShouldBeFalse();

        // Ramura „nu am cont, am nevoie de unul" trimite omul la bancă, iar contul apare zile mai
        // târziu. Cerut aici, ținea pasul deschis la nesfârșit: butonul de trimitere la verificare
        // nu apărea, iar validarea adminului nu avea ce închide.
        PfaRegistration withoutBank = FiscalRegistration();
        withoutBank.BankAccountDeclaration = null;
        OnboardingStepCatalog.FiscalUserPartComplete(withoutBank).ShouldBeTrue();
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
        // Contul declarat de mână, cu extrasul încărcat: drumul obișnuit. Fixtura cerea înainte
        // `Verified`, un status pe care îl pune doar potrivirea cu un cont legat prin Open Banking
        // — așa a trecut testul ani de zile peste un pas pe care nimeni nu-l putea închide.
        registration.BankAccountDeclaration = new PfaBankAccountDeclaration
        {
            Id = Guid.NewGuid(),
            Source = BankDeclarationSource.Manual,
            Status = BankDeclarationStatus.Pending,
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

    /// <summary>Eligibilitate validată din admin — singurul lucru care bifează pasul 1.</summary>
    private static OnboardingEligibilityProfile EligibleProfile() =>
        new() { Id = Guid.NewGuid(), Status = EligibilityStatus.Eligible, AdminValidatedAtUtc = DateTime.UtcNow };

    private static readonly DocumentCategory[] EligibilityUploads =
        [DocumentCategory.CarteIdentitate, DocumentCategory.PermisConducere, DocumentCategory.AtestatSofer];

    private static List<Document> EligibilityDocuments(DateTime? uploadedAtUtc = null) =>
        EligibilityUploads
            .Select(c =>
            {
                Document d = Uploaded(c, DocumentStatus.Pending);
                d.UploadedAtUtc = uploadedAtUtc ?? d.UploadedAtUtc;
                return d;
            })
            .ToList();

    /* ── Validarea din admin: clepsidră cât se verifică, bifă după ── */

    /// <summary>
    /// Actele încărcate pun pasul 1 în verificare, oricât de bine sau prost s-au citit datele.
    /// Bifa vine doar din admin; iar pasul următor se deschide oricum, ca nimeni să nu aștepte.
    /// </summary>
    [Fact]
    public void Eligibility_WithDocumentsUploaded_IsPendingAdminAndUnlocksPfa()
    {
        var profile = new OnboardingEligibilityProfile { Id = Guid.NewGuid(), Status = EligibilityStatus.Eligible };

        List<OnboardingStepDto> steps = OnboardingStepCatalog.BuildSteps(
            null, OnboardingSectionStatus.InProgress, profile, EligibilityDocuments());

        steps[0].State.ShouldBe(OnboardingStepCatalog.States.PendingAdmin);
        steps[0].UserPartDone.ShouldBeTrue();
        steps[1].Status.ShouldNotBe(Locked);
    }

    [Fact]
    public void Eligibility_ValidatedByAdmin_IsCompleted()
    {
        List<OnboardingStepDto> steps = OnboardingStepCatalog.BuildSteps(
            null, OnboardingSectionStatus.InProgress, EligibleProfile(), EligibilityDocuments());

        steps[0].Status.ShouldBe(Completed);
    }

    [Fact]
    public void Eligibility_RejectedByAdmin_ReturnsToTheDriverUntilHeReuploads()
    {
        DateTime rejectedAt = DateTime.UtcNow;
        var profile = new OnboardingEligibilityProfile
        {
            Id = Guid.NewGuid(),
            Status = EligibilityStatus.Eligible,
            AdminRejectedAtUtc = rejectedAt,
            AdminReviewNote = "Permisul e expirat.",
        };

        List<OnboardingStepDto> rejected = OnboardingStepCatalog.BuildSteps(
            null, OnboardingSectionStatus.InProgress, profile, EligibilityDocuments(rejectedAt.AddHours(-1)));

        rejected[0].State.ShouldBe(OnboardingStepCatalog.States.Rejected);
        OnboardingStepCatalog.CurrentStepKey(rejected).ShouldBe("eligibility");

        List<OnboardingStepDto> reuploaded = OnboardingStepCatalog.BuildSteps(
            null, OnboardingSectionStatus.InProgress, profile, EligibilityDocuments(rejectedAt.AddMinutes(5)));

        reuploaded[0].State.ShouldBe(OnboardingStepCatalog.States.PendingAdmin);
    }

    /// <summary>
    /// Bugul raportat: „am validat secțiunea din admin și îmi scrie că nu e validată". Pasul ARR se
    /// închidea doar pe autorizația emisă, pe care n-o înregistra nimeni.
    /// </summary>
    [Fact]
    public void Arr_SectionValidatedByAdmin_CompletesTheStep()
    {
        PfaRegistration registration = ReadyForPlatforms();
        registration.ArrAuthorizationRequest!.Status = ArrAuthorizationStatus.Submitted;
        registration.OnboardingSections.Add(new OnboardingSectionApproval
        {
            Id = Guid.NewGuid(),
            SectionKey = OnboardingSectionKey.AutorizatieTransport,
            Status = OnboardingSectionStatus.Validated,
        });

        List<OnboardingStepDto> steps = Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[3].Status.ShouldBe(Completed);
    }

    [Fact]
    public void Arr_DossierSubmitted_IsPendingAdmin()
    {
        PfaRegistration registration = ReadyForPlatforms();
        registration.ArrAuthorizationRequest!.Status = ArrAuthorizationStatus.Submitted;

        List<OnboardingStepDto> steps = Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[3].State.ShouldBe(OnboardingStepCatalog.States.PendingAdmin);
        steps[4].Status.ShouldNotBe(Locked);
    }

    /// <summary>Credențialele complete pun pasul în verificare — nu-l mai bifează singure.</summary>
    [Fact]
    public void Platforms_WithCredentials_IsPendingAdminUntilActivated()
    {
        PfaRegistration registration = ReadyForPlatforms();
        PfaPlatformAccount account = CompletePlatformAccount(PfaPlatformProvider.Bolt);
        registration.PlatformAccounts.Add(account);

        Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated)[4]
            .State.ShouldBe(OnboardingStepCatalog.States.PendingAdmin);

        account.OnboardingStatus = PfaPlatformOnboardingStatus.Active;

        Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated)[4]
            .Status.ShouldBe(Completed);
    }

    /// <summary>
    /// Dosarul copiei conforme depus încheie partea șoferului pe ultimul pas: fără asta nu exista
    /// „am terminat", iar ecranul de final nu apărea niciodată.
    /// </summary>
    [Fact]
    public void Vehicle_DossierSubmitted_EndsTheDriversPartAndAwaitsAdmin()
    {
        PfaRegistration registration = ReadyForPlatforms();
        registration.PlatformAccounts.Add(CompletePlatformAccount(PfaPlatformProvider.Bolt));
        registration.Vehicles.Add(new PfaVehicle
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            CopyRequest = new VehicleCopyRequest
            {
                Id = Guid.NewGuid(),
                Status = VehicleCopyRequestStatus.Submitted,
                SubmittedAtUtc = DateTime.UtcNow,
            },
        });

        List<OnboardingStepDto> steps = Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated);

        steps[5].State.ShouldBe(OnboardingStepCatalog.States.PendingAdmin);
        OnboardingStepCatalog.CurrentStepKey(steps).ShouldBeNull();
        OnboardingStepCatalog.AllCompleted(steps).ShouldBeFalse();
    }

    [Fact]
    public void Vehicle_BothSectionsValidated_CompletesTheStep()
    {
        PfaRegistration registration = ReadyForPlatforms();
        registration.PlatformAccounts.Add(CompletePlatformAccount(PfaPlatformProvider.Bolt));
        registration.Vehicles.Add(new PfaVehicle
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            CopyRequest = new VehicleCopyRequest { Id = Guid.NewGuid(), SubmittedAtUtc = DateTime.UtcNow },
        });
        foreach (OnboardingSectionKey key in new[] { OnboardingSectionKey.CopieConforma, OnboardingSectionKey.Vehicul })
        {
            registration.OnboardingSections.Add(new OnboardingSectionApproval
            {
                Id = Guid.NewGuid(),
                SectionKey = key,
                Status = OnboardingSectionStatus.Validated,
            });
        }

        Build(registration, EligibleProfile(), OnboardingSectionStatus.Validated)[5].Status.ShouldBe(Completed);
    }

    /* ── Partea șoferului la pasul 2, pe ramura „am deja PFA" ── */

    /// <summary>
    /// Cu un singur certificat, pasul e încă al șoferului.
    ///
    /// Contează în două locuri deodată: secțiunea nu trece în „așteaptă validarea" (deci ecranul de
    /// așteptare nu se pune peste certificate și rezumat), iar adminul nu poate aproba dosarul —
    /// aprobarea închide pasul, iar un pas închis nu mai acceptă scrieri, deci constatatorul n-ar
    /// mai avea pe unde intra niciodată.
    /// </summary>
    [Fact]
    public void PfaUserPartDone_WithOnlyTheRegistrationCertificate_IsFalse()
    {
        OnboardingStepCatalog
            .PfaUserPartDone(Registration(), [Uploaded(DocumentCategory.CertificatInregistrare)])
            .ShouldBeFalse();
    }

    [Fact]
    public void PfaUserPartDone_WithBothCertificates_IsTrue()
    {
        OnboardingStepCatalog
            .PfaUserPartDone(
                Registration(),
                [
                    Uploaded(DocumentCategory.CertificatInregistrare),
                    Uploaded(DocumentCategory.CertificatConstatator),
                ])
            .ShouldBeTrue();
    }

    /// <summary>Un certificat respins nu contează ca încărcat — altfel poarta s-ar deschide pe un act refuzat.</summary>
    [Fact]
    public void PfaUserPartDone_WithARejectedConstatator_IsFalse()
    {
        OnboardingStepCatalog
            .PfaUserPartDone(
                Registration(),
                [
                    Uploaded(DocumentCategory.CertificatInregistrare),
                    Uploaded(DocumentCategory.CertificatConstatator, DocumentStatus.Rejected),
                ])
            .ShouldBeFalse();
    }

    /// <summary>Ramura „Nu am PFA" n-are certificate de încărcat: poarta nu i se aplică.</summary>
    [Fact]
    public void PfaUserPartDone_OnTheFormationBranch_IsTrue()
    {
        var registration = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            RegistrationType = RegistrationType.NuAmPfa,
        };

        OnboardingStepCatalog.PfaUserPartDone(registration, []).ShouldBeTrue();
    }

    private static Document Uploaded(
        DocumentCategory category,
        DocumentStatus status = DocumentStatus.Verified) =>
        new()
        {
            Id = Guid.NewGuid(),
            Category = category,
            Status = status,
            UploadedAtUtc = DateTime.UtcNow,
        };

    /// <summary>Dosar „Am PFA” gol: pasul PFA depinde doar de statusul secțiunii.</summary>
    private static PfaRegistration Registration() =>
        new() { Id = Guid.NewGuid(), RegistrationType = RegistrationType.AmPfa };
}
