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
        Pensioner = "no",
        Student = "no",
        EmployedFullTime = "no",
    };

    // ── Schema ────────────────────────────────────────────────────────────────

    [Fact]
    public void Profilul_are_doar_trei_situatii()
    {
        FiscalProfileSchema.Situations.Select(s => s.Key).ShouldBe(["pensioner", "student", "employedFullTime"]);
    }

    [Fact]
    public void Niciuna_inseamna_toate_trei_nu_si_e_un_raspuns_complet() =>
        FiscalProfileSchema.Validate(Complete(), requireAll: true).ShouldBeEmpty();

    [Fact]
    public void Fara_raspuns_nu_se_poate_confirma()
    {
        Dictionary<string, string> errors = FiscalProfileSchema.Validate(new FiscalProfileAnswers { Pensioner = "yes" }, requireAll: true);

        errors.Keys.ShouldBe(["student", "employedFullTime"], ignoreOrder: true);
        errors["student"].ShouldBe(FiscalProfileSchema.ChooseAnswer);
    }

    [Fact]
    public void Ciorna_accepta_raspunsuri_lipsa_dar_nu_optiuni_inventate()
    {
        FiscalProfileSchema.Validate(new FiscalProfileAnswers { Pensioner = "yes" }, requireAll: false).ShouldBeEmpty();
        FiscalProfileSchema.Validate(new FiscalProfileAnswers { Student = "nu_stiu" }, requireAll: false).ShouldContainKey("student");
    }

    [Theory]
    [InlineData("no", "no", "no", "Standard")]
    [InlineData("yes", "no", "yes", "Pensionar · Angajat")]
    [InlineData("no", "yes", "no", "Student")]
    public void Eticheta_spune_situatia(string pensioner, string student, string employed, string label) =>
        FiscalProfileSchema.Label(new FiscalProfileAnswers { Pensioner = pensioner, Student = student, EmployedFullTime = employed })
            .ShouldBe(label);

    /// <summary>Profilurile completate cu formularul vechi rămân valabile: întrebările scoase se ignoră.</summary>
    [Fact]
    public void Un_profil_vechi_se_citeste_fara_intrebarile_scoase()
    {
        FiscalProfileAnswers old = FiscalProfileService.Deserialize(
            """{"dataCorrect":"yes","employment":"full","salaryAboveCassMin":"yes","pensioner":"yes","student":"no","crossBorder":"no","carriedLosses":"yes"}""");

        old.ShouldBe(new FiscalProfileAnswers { Pensioner = "yes", Student = "no", EmployedFullTime = "yes" });
        FiscalProfileService.Deserialize("""{"employment":"part"}""").EmployedFullTime.ShouldBe("no");
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
            .Handle(new SaveFiscalProfileDraftCommand(2026, new FiscalProfileAnswers { Pensioner = "no" }, null), default);
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

    // ── Staff ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Staff_alege_situatia_cu_motiv_si_deblocheaza_estimarile()
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
        // Selectorul rapid din lista de clienți: cu situația aleasă, profilul e complet.
        edited.Value.Status.ShouldBe("COMPLETED");
        edited.Value.EstimatedTaxesUnlockedAtUtc.ShouldBe(Now);
        edited.Value.Revision.ShouldBe(1);

        FiscalProfileRevisionResponse revision = (await new GetFiscalProfileRevisionsQueryHandler(db, service)
            .Handle(new GetFiscalProfileRevisionsQuery(FiscalProfileScope.Admin, pfa.Id, 2026), default)).Value.Single();
        revision.Actor.Role.ShouldBe("admin");
        revision.Reason.ShouldBe("Discutat telefonic");
        revision.Changes.ShouldContain(c => c.Field == "employedFullTime" && c.OldValue == null && c.NewValue == "no");
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
            .Handle(new SaveFiscalProfileDraftCommand(2026, new FiscalProfileAnswers { Pensioner = "no" }, null), default);
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

        // QA 16: o recalculare cu aceleași date (doar marcată „stale”, ca la fiecare trecere a jobului) nu
        // adaugă o rulare nouă în istoric; ultima redevine valabilă.
        for (int pass = 0; pass < 3; pass++)
        {
            await FiscalEstimateInvalidation.MarkStaleAsync(db, pfa.Id, 2026, DateTime.UtcNow, default);
            await db.SaveChangesAsync();
            await recalculate.Handle(new RecalculateEstimatedTaxesCommand(pfa.Id, 2026), default);
        }

        (await db.FiscalEstimateRuns.CountAsync()).ShouldBe(1);
        (await db.FiscalEstimateRuns.SingleAsync()).Stale.ShouldBeFalse();

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
        new(db, new StubUser(userId), new NoLookup(), new FixedClock(), NullLogger<FiscalProfileService>.Instance);

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
