using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.FiscalEstimates;
using Application.FiscalProfiles;
using Domain.FiscalEstimates;
using Domain.FiscalProfiles;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.FiscalProfiles;

/// <summary>
/// Profilul fiscal anual al PFA-ului (SPEC_PROFIL_FISCAL_PFA): schema formularului, confirmarea
/// care deblochează estimările, editarea multi-rol, concurența și reamintirile.
/// </summary>
public sealed class FiscalProfileTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc);

    private static FiscalProfileAnswers Complete() => new()
    {
        DataCorrect = "yes",
        Employment = "none",
        Pensioner = "no",
        Student = "no",
        OwnPensionSystem = "no",
        PrivateContact = "no",
        OtherIndependent = "no",
        OtherIncome = "no",
        TaxPaymentsMade = "no",
        CassOptIn = "no",
        CasVoluntary = "no",
        CarriedLosses = "no",
        CrossBorder = "no",
    };

    private static readonly FiscalProfileConditions NoConditions = new(false, null, null, false);

    // ── Schema ────────────────────────────────────────────────────────────────

    [Fact]
    public void Nu_stiu_doar_la_CASS_pe_alte_venituri_si_nu_se_intreaba_de_norma_de_venit()
    {
        // Singura excepție: dacă plătește deja CASS pe chirii/dividende — acolo „Nu știu” trimite
        // întrebarea la contabil. Restul întrebărilor au doar răspunsuri pe care PFA-ul le știe.
        foreach (FiscalProfileSchema.Question question in FiscalProfileSchema.Questions.Where(q => q.Key != "otherIncomeCassInsured"))
        {
            question.Options.ShouldNotContain(o => o.Contains("stiu", StringComparison.OrdinalIgnoreCase) || o == "unknown");
            question.Key.ShouldNotContain("norma", Case.Insensitive);
        }
    }

    [Fact]
    public void Campurile_ascunse_se_sterg_si_nu_blocheaza_validarea()
    {
        FiscalProfileAnswers answers = Complete() with
        {
            Employment = "none",
            EmploymentStart = new DateOnly(2025, 1, 1),
            Pensioner = "no",
            PensionerSince = new DateOnly(2020, 1, 1),
            CorrectionDetails = "rămas de la varianta „Nu”",
            PriorDocs = "have",
            CarriedLosses = "yes",
        };

        FiscalProfileAnswers normalized = FiscalProfileSchema.Normalize(answers, NoConditions);

        normalized.EmploymentStart.ShouldBeNull();
        normalized.PensionerSince.ShouldBeNull();
        normalized.CorrectionDetails.ShouldBeNull();
        normalized.PriorDocs.ShouldBeNull();
        normalized.CarriedLosses.ShouldBeNull();
        FiscalProfileSchema.Validate(normalized, NoConditions, requireAll: true).ShouldBeEmpty();
    }

    [Fact]
    public void Campurile_conditionate_vizibile_sunt_obligatorii()
    {
        var conditions = new FiscalProfileConditions(true, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 14), true);
        FiscalProfileAnswers answers = Complete() with { DataCorrect = "no", Employment = "full", Pensioner = "yes", PriorDocs = "have", CarriedLosses = null };

        Dictionary<string, string> errors = FiscalProfileSchema.Validate(answers, conditions, requireAll: true);

        errors["correctionDetails"].ShouldBe(FiscalProfileSchema.FillText);
        errors["employmentStart"].ShouldBe(FiscalProfileSchema.FillDate);
        errors["pensionerSince"].ShouldBe(FiscalProfileSchema.FillDate);
        errors["priorDocsLocation"].ShouldBe(FiscalProfileSchema.ChooseAnswer);
        errors["carriedLosses"].ShouldBe(FiscalProfileSchema.ChooseAnswer);
        errors.ShouldNotContainKey("employmentEnd");
        errors.ShouldNotContainKey("notes");
    }

    [Fact]
    public void Ciorna_accepta_raspunsuri_lipsa_dar_nu_optiuni_inventate()
    {
        FiscalProfileSchema.Validate(new FiscalProfileAnswers { Employment = "full" }, NoConditions, requireAll: false).ShouldBeEmpty();
        FiscalProfileSchema.Validate(new FiscalProfileAnswers { Employment = "nu_stiu" }, NoConditions, requireAll: false)
            .ShouldContainKey("employment");
    }

    // ── Întrebările condiționate ─────────────────────────────────────────────

    [Fact]
    public void PFA_existent_intrat_recent_are_interval_neacoperit()
    {
        FiscalProfileConditions c = FiscalProfileService.ComputeConditions(
            2026, new DateOnly(2019, 5, 2), PfaSource.Existing, new DateOnly(2026, 3, 15), null);

        c.AskPriorDocs.ShouldBeTrue();
        c.PriorFrom.ShouldBe(new DateOnly(2026, 1, 1));
        c.PriorTo.ShouldBe(new DateOnly(2026, 3, 14));
        c.AskCarriedLosses.ShouldBeTrue();
    }

    [Fact]
    public void PFA_nou_infiintat_dupa_acces_nu_primeste_intrebarile_de_interval_si_pierderi()
    {
        FiscalProfileConditions c = FiscalProfileService.ComputeConditions(
            2026, new DateOnly(2026, 4, 10), PfaSource.ViaPartner, new DateOnly(2026, 3, 15), null);

        c.AskPriorDocs.ShouldBeFalse();
        c.AskCarriedLosses.ShouldBeFalse();
    }

    [Fact]
    public void Luna_procesata_de_contabil_muta_inceputul_acoperirii()
    {
        FiscalProfileConditions c = FiscalProfileService.ComputeConditions(
            2026, new DateOnly(2018, 1, 1), PfaSource.Existing, new DateOnly(2026, 6, 20), new DateOnly(2026, 2, 1));

        c.PriorTo.ShouldBe(new DateOnly(2026, 1, 31));
    }

    [Fact]
    public void Acces_din_anul_trecut_inseamna_an_acoperit()
    {
        FiscalProfileService.ComputeConditions(2026, new DateOnly(2018, 1, 1), PfaSource.Existing, new DateOnly(2025, 6, 1), null)
            .AskPriorDocs.ShouldBeFalse();
    }

    // ── Fluxul PFA ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirmarea_deblocheaza_estimarile_si_inchide_sarcina_de_apel()
    {
        await using ApplicationDbContext db = NewDb();
        (User owner, PfaRegistration pfa) = Seed(db);
        db.AdminCallTasks.Add(new AdminCallTask { Id = Guid.NewGuid(), PfaRegistrationId = pfa.Id, TaxYear = 2026 });
        await db.SaveChangesAsync();
        FiscalProfileService service = Service(db, owner.Id);

        Result<FiscalProfileResponse> draft = await new SaveFiscalProfileDraftCommandHandler(service)
            .Handle(new SaveFiscalProfileDraftCommand(2026, new FiscalProfileAnswers { Employment = "none" }, null), default);
        draft.Value.Status.ShouldBe("DRAFT");

        (await new GetEstimatedTaxesQueryHandler(db, service).Handle(new GetEstimatedTaxesQuery(FiscalProfileScope.Pfa, null, 2026), default))
            .Value.Locked.ShouldBeTrue();

        Result<FiscalProfileResponse> missingConfirmation = await new CompleteFiscalProfileCommandHandler(db, service)
            .Handle(new CompleteFiscalProfileCommand(2026, Complete(), false, draft.Value.Revision), default);
        missingConfirmation.IsFailure.ShouldBeTrue();

        Result<FiscalProfileResponse> completed = await new CompleteFiscalProfileCommandHandler(db, service)
            .Handle(new CompleteFiscalProfileCommand(2026, Complete(), true, draft.Value.Revision), default);

        completed.IsSuccess.ShouldBeTrue();
        completed.Value.Status.ShouldBe("COMPLETED");
        completed.Value.EstimatedTaxesUnlockedAtUtc.ShouldBe(Now);
        (await new GetEstimatedTaxesQueryHandler(db, service).Handle(new GetEstimatedTaxesQuery(FiscalProfileScope.Pfa, null, 2026), default))
            .Value.Locked.ShouldBeFalse();
        (await db.AdminCallTasks.SingleAsync()).State.ShouldBe(AdminCallTaskState.ResolvedByCompletion);
        (await db.PfaTaxProfileRevisions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Raspunsul_Nu_la_date_deschide_o_cerere_de_corectare_fara_sa_atinga_sursa()
    {
        await using ApplicationDbContext db = NewDb();
        (User owner, PfaRegistration pfa) = Seed(db);
        FiscalProfileService service = Service(db, owner.Id);

        await new CompleteFiscalProfileCommandHandler(db, service).Handle(
            new CompleteFiscalProfileCommand(2026, Complete() with { DataCorrect = "no", CorrectionDetails = "Activitatea a început în iunie." }, true, null),
            default);

        PfaDataCorrectionRequest correction = await db.PfaDataCorrectionRequests.SingleAsync();
        correction.Details.ShouldBe("Activitatea a început în iunie.");
        correction.PfaRegistrationId.ShouldBe(pfa.Id);
        (await db.PfaRegistrations.SingleAsync()).Cui.ShouldBe("12345678");
    }

    // ── Staff ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Staff_editeaza_cu_motiv_obligatoriu_si_nu_poate_completa_profilul()
    {
        await using ApplicationDbContext db = NewDb();
        (_, PfaRegistration pfa) = Seed(db);
        User admin = AddUser(db, UserRole.Admin);
        await db.SaveChangesAsync();
        FiscalProfileService service = Service(db, admin.Id);
        var handler = new EditFiscalProfileCommandHandler(db, service);

        Result<FiscalProfileResponse> noReason = await handler.Handle(
            new EditFiscalProfileCommand(FiscalProfileScope.Admin, pfa.Id, 2026, Complete(), null, 0), default);
        noReason.IsFailure.ShouldBeTrue();

        Result<FiscalProfileResponse> edited = await handler.Handle(
            new EditFiscalProfileCommand(FiscalProfileScope.Admin, pfa.Id, 2026, Complete(), "Discutat telefonic", 0), default);

        edited.IsSuccess.ShouldBeTrue();
        // Toate răspunsurile date de staff, dar tot ciornă: doar PFA-ul confirmă.
        edited.Value.Status.ShouldBe("DRAFT");
        edited.Value.Revision.ShouldBe(1);

        FiscalProfileRevisionResponse revision = (await new GetFiscalProfileRevisionsQueryHandler(db, service)
            .Handle(new GetFiscalProfileRevisionsQuery(FiscalProfileScope.Admin, pfa.Id, 2026), default)).Value.Single();
        revision.Actor.Role.ShouldBe("admin");
        revision.Reason.ShouldBe("Discutat telefonic");
        revision.Changes.ShouldContain(c => c.Field == "employment" && c.OldValue == null && c.NewValue == "none");
    }

    [Fact]
    public async Task Editarea_simultana_raspunde_cu_conflict()
    {
        await using ApplicationDbContext db = NewDb();
        (_, PfaRegistration pfa) = Seed(db);
        User admin = AddUser(db, UserRole.Admin);
        await db.SaveChangesAsync();
        var handler = new EditFiscalProfileCommandHandler(db, Service(db, admin.Id));

        (await handler.Handle(new EditFiscalProfileCommand(FiscalProfileScope.Admin, pfa.Id, 2026, Complete(), "prima", 0), default))
            .IsSuccess.ShouldBeTrue();

        Result<FiscalProfileResponse> stale = await handler.Handle(
            new EditFiscalProfileCommand(FiscalProfileScope.Admin, pfa.Id, 2026, Complete() with { Student = "yes" }, "a doua, din versiunea veche", 0),
            default);

        stale.IsFailure.ShouldBeTrue();
        stale.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task Contabilul_vede_doar_clientii_lui_iar_un_PFA_doar_profilul_propriu()
    {
        await using ApplicationDbContext db = NewDb();
        (_, PfaRegistration pfa) = Seed(db);
        (User other, _) = Seed(db, "12121212");
        User contabil = AddUser(db, UserRole.Contabil);
        await db.SaveChangesAsync();

        (await new GetFiscalProfileQueryHandler(Service(db, contabil.Id))
            .Handle(new GetFiscalProfileQuery(FiscalProfileScope.Accounting, pfa.Id, 2026), default))
            .Error.Type.ShouldBe(ErrorType.NotFound);

        pfa.AssignedContabilId = contabil.Id;
        await db.SaveChangesAsync();
        (await new GetFiscalProfileQueryHandler(Service(db, contabil.Id))
            .Handle(new GetFiscalProfileQuery(FiscalProfileScope.Accounting, pfa.Id, 2026), default))
            .IsSuccess.ShouldBeTrue();

        // Un PFA nu poate ajunge la profilul altuia: ruta lui nu primește un id.
        Result<FiscalProfileResponse> own = await new GetFiscalProfileQueryHandler(Service(db, other.Id))
            .Handle(new GetFiscalProfileQuery(FiscalProfileScope.Pfa, pfa.Id, 2026), default);
        own.Value.PfaRegistrationId.ShouldNotBe(pfa.Id);

        (await new GetFiscalProfileQueryHandler(Service(db, other.Id))
            .Handle(new GetFiscalProfileQuery(FiscalProfileScope.Admin, pfa.Id, 2026), default))
            .IsFailure.ShouldBeTrue();
    }

    // ── Reamintiri ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    [InlineData(13, 1)]
    [InlineData(14, 2)]
    [InlineData(35, 5)]
    public void O_reamintire_pe_saptamana_de_la_ziua_7(int days, int week)
    {
        var start = new DateOnly(2026, 3, 1);
        FiscalProfileReminderSchedule.WeekIndex(start, start.AddDays(days)).ShouldBe(week);
    }

    [Fact]
    public async Task Jobul_trimite_o_reamintire_pe_saptamana_si_o_singura_sarcina_de_apel()
    {
        await using ApplicationDbContext db = NewDb();
        (_, PfaRegistration pfa) = Seed(db);
        pfa.OnboardingCompletedAtUtc = Now.AddDays(-31);
        await db.SaveChangesAsync();
        var handler = new RunFiscalProfileRemindersCommandHandler(db, Service(db, Guid.Empty), NullLogger<RunFiscalProfileRemindersCommandHandler>.Instance);

        (await handler.Handle(new RunFiscalProfileRemindersCommand(), default)).Value.ShouldBe(new FiscalProfileRemindersRun(1, 1));
        // A doua rulare în aceeași zi (sau săptămână) nu mai trimite nimic.
        (await handler.Handle(new RunFiscalProfileRemindersCommand(), default)).Value.ShouldBe(new FiscalProfileRemindersRun(0, 0));

        (await db.Notifications.SingleAsync()).Text.ShouldBe(RunFiscalProfileRemindersCommandHandler.ReminderText);
        (await db.AdminCallTasks.SingleAsync()).Reason.ShouldBe(AdminCallTask.ProfileIncomplete30Days);
    }

    [Fact]
    public async Task Profilul_completat_opreste_reamintirile()
    {
        await using ApplicationDbContext db = NewDb();
        (_, PfaRegistration pfa) = Seed(db);
        pfa.OnboardingCompletedAtUtc = Now.AddDays(-8);
        db.PfaTaxProfiles.Add(new PfaTaxProfile
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa.Id,
            TaxYear = 2026,
            Status = PfaTaxProfileStatus.Completed,
        });
        await db.SaveChangesAsync();

        FiscalProfileRemindersRun run = (await new RunFiscalProfileRemindersCommandHandler(
            db, Service(db, Guid.Empty), NullLogger<RunFiscalProfileRemindersCommandHandler>.Instance)
            .Handle(new RunFiscalProfileRemindersCommand(), default)).Value;

        run.ShouldBe(new FiscalProfileRemindersRun(0, 0));
    }

    // ── Motorul de taxe estimate: rulări ─────────────────────────────────────

    /// <summary>
    /// Bugul raportat: „Estimările nu sunt încă disponibile pentru 2026", deși parametrii anului
    /// existau. Rularea salvată înainte să apară parametrii era din ziua curentă, deci jobul n-o
    /// relua până a doua zi. O rulare calculată cu alți parametri decât cei curenți e depășită.
    /// </summary>
    [Theory]
    [InlineData(TaxStatuses.RuleUnavailable, null, true)]
    [InlineData("OK", "2025.9", true)]
    [InlineData("OK", "2026.1", false)]
    [InlineData(TaxStatuses.Error, null, false)]
    public async Task Jobul_reia_rularile_calculate_cu_alti_parametri(string status, string? ruleVersion, bool expectedDue)
    {
        await using ApplicationDbContext db = NewDb();
        (_, PfaRegistration pfa) = Seed(db);
        db.PfaTaxProfiles.Add(new PfaTaxProfile
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa.Id,
            TaxYear = 2026,
            Status = PfaTaxProfileStatus.Completed,
        });
        db.FiscalEstimateRuns.Add(new FiscalEstimateRun
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa.Id,
            TaxYear = 2026,
            AsOf = DateOnly.FromDateTime(FiscalProfileService.ToRomania(Now)),
            Status = status,
            RuleVersion = ruleVersion,
            CreatedAtUtc = Now.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var recalculate = new RecordingRecalculate();
        int done = (await new ProcessFiscalEstimateQueueCommandHandler(
                db, recalculate, new TaxYearParametersProvider(), new FixedClock(),
                NullLogger<ProcessFiscalEstimateQueueCommandHandler>.Instance)
            .Handle(new ProcessFiscalEstimateQueueCommand(), default)).Value;

        recalculate.Calls.Count.ShouldBe(expectedDue ? 1 : 0);
        done.ShouldBe(expectedDue ? 1 : 0);
    }

    private sealed class RecordingRecalculate : ICommandHandler<RecalculateEstimatedTaxesCommand, Guid?>
    {
        public List<RecalculateEstimatedTaxesCommand> Calls { get; } = [];

        public Task<Result<Guid?>> Handle(RecalculateEstimatedTaxesCommand command, CancellationToken cancellationToken)
        {
            Calls.Add(command);
            return Task.FromResult(Result.Success<Guid?>(Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task Rularea_porneste_doar_pe_profil_completat_si_expira_la_editare()
    {
        await using ApplicationDbContext db = NewDb();
        (User owner, PfaRegistration pfa) = Seed(db);
        User admin = AddUser(db, UserRole.Admin);
        for (int month = 1; month <= 8; month++)
        {
            db.PfaMonthlyIncomes.Add(new PfaMonthlyIncome { Id = Guid.NewGuid(), PfaRegistrationId = pfa.Id, Year = 2026, Month = month, VenitBolt = 2_000 });
        }

        await db.SaveChangesAsync();
        FiscalProfileService service = Service(db, owner.Id);
        var recalculate = new RecalculateEstimatedTaxesCommandHandler(
            db, new FinancialSnapshotProvider(db), new TaxYearParametersProvider(), new TaxEngine2026(), new FixedClock());

        // Ciornă: endpointul e blocat și nu se creează nicio rulare.
        await new SaveFiscalProfileDraftCommandHandler(service)
            .Handle(new SaveFiscalProfileDraftCommand(2026, new FiscalProfileAnswers { Employment = "none" }, null), default);
        (await recalculate.Handle(new RecalculateEstimatedTaxesCommand(pfa.Id, 2026), default)).Value.ShouldBeNull();
        (await db.FiscalEstimateRuns.CountAsync()).ShouldBe(0);

        await new CompleteFiscalProfileCommandHandler(db, service)
            .Handle(new CompleteFiscalProfileCommand(2026, Complete(), true, null), default);

        // Confirmat, dar încă necalculat: „se calculează”, fără cifre.
        EstimatedTaxesResponse pending = (await new GetEstimatedTaxesQueryHandler(db, service)
            .Handle(new GetEstimatedTaxesQuery(FiscalProfileScope.Pfa, null, 2026), default)).Value;
        pending.Status.ShouldBe(TaxStatuses.Calculating);
        pending.Components!.ShouldAllBe(c => c.Amount == null);

        await recalculate.Handle(new RecalculateEstimatedTaxesCommand(pfa.Id, 2026), default);
        EstimatedTaxesResponse done = (await new GetEstimatedTaxesQueryHandler(db, service)
            .Handle(new GetEstimatedTaxesQuery(FiscalProfileScope.Pfa, null, 2026), default)).Value;
        done.Stale.ShouldBeFalse();
        // 8 luni × 2.000 realizat + media săptămânală din ultimele luni × săptămânile rămase.
        done.Projection!.NetRealized.ShouldBe(16_000);
        done.Components!.Single(c => c.Component == TaxComponents.Cass).Status.ShouldBe(TaxStatuses.Estimated);
        done.Components!.Single(c => c.Component == TaxComponents.PlatformTaxes).Status.ShouldBe(TaxStatuses.NotConfigured);
        done.Reserve!.Weekly.ShouldNotBeNull();
        done.Components!.ShouldAllBe(c => c.Breakdown == null);

        // Editarea staff-ului: rularea veche expiră, iar cea nouă poartă revizia nouă.
        int revisionBefore = (await db.FiscalEstimateRuns.SingleAsync()).ProfileRevision;
        await new EditFiscalProfileCommandHandler(db, Service(db, admin.Id)).Handle(
            new EditFiscalProfileCommand(FiscalProfileScope.Admin, pfa.Id, 2026, Complete() with { Student = "yes" }, "Corectat la telefon", revisionBefore),
            default);
        (await db.FiscalEstimateRuns.SingleAsync()).Stale.ShouldBeTrue();
        (await new GetEstimatedTaxesQueryHandler(db, service)
            .Handle(new GetEstimatedTaxesQuery(FiscalProfileScope.Pfa, null, 2026), default)).Value.Status.ShouldBe(TaxStatuses.Calculating);

        await recalculate.Handle(new RecalculateEstimatedTaxesCommand(pfa.Id, 2026), default);
        FiscalEstimateRun latest = await db.FiscalEstimateRuns.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.ProfileRevision).FirstAsync(r => !r.Stale);
        latest.ProfileRevision.ShouldBe(revisionBefore + 1);

        EstimatedTaxesResponse staff = (await new GetEstimatedTaxesQueryHandler(db, Service(db, admin.Id))
            .Handle(new GetEstimatedTaxesQuery(FiscalProfileScope.Admin, pfa.Id, 2026), default)).Value;
        staff.RuleVersion.ShouldBe("2026.1");
        staff.Components!.Single(c => c.Component == TaxComponents.Cass).Breakdown.ShouldNotBeNull();
        staff.Runs!.Count.ShouldBe(2);
    }

    // ── Infrastructură de test ───────────────────────────────────────────────

    private static (User Owner, PfaRegistration Pfa) Seed(ApplicationDbContext db, string cui = "12345678")
    {
        User owner = AddUser(db, UserRole.Client);
        var pfa = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            User = owner,
            Cui = cui,
            LegalName = "POPESCU ION PFA",
            PfaSource = PfaSource.Existing,
            Status = PfaRegistrationStatus.Approved,
            OnboardingCompletedAtUtc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        db.PfaRegistrations.Add(pfa);
        db.SaveChanges();
        return (owner, pfa);
    }

    private static User AddUser(ApplicationDbContext db, UserRole role)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@test.ro",
            FirstName = "Test",
            LastName = role.ToString(),
            Role = role,
        };
        db.Users.Add(user);
        return user;
    }

    private static FiscalProfileService Service(ApplicationDbContext db, Guid userId) =>
        new(db, new StubUser(userId), new NoLookup(), new FixedClock(), NullLogger<FiscalProfileService>.Instance, new TaxYearParametersProvider());

    private static ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private sealed class StubUser(Guid id) : IUserContext
    {
        public Guid UserId { get; } = id;
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => Now;
    }

    private sealed class NoLookup : ICompanyLookupService
    {
        public Task<CompanyLookupResult?> FindByCuiAsync(string cui, CancellationToken cancellationToken = default) =>
            Task.FromResult<CompanyLookupResult?>(null);
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
