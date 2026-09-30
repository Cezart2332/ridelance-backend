using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.Documents;
using Application.Accounting.Months;
using Domain.Accounting;
using Infrastructure.Accounting.Anaf;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// Interogările modulului, executate pe Postgres: EF InMemory evaluează totul în memorie și ar
/// ascunde un LINQ pe care Npgsql nu-l poate traduce. Rulează doar cu
/// <c>RIDELANCE_TEST_DATABASE</c> setat (o bază migrată, de test); altfel nu face nimic.
/// </summary>
public sealed class PostgresQueryTranslationTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("RIDELANCE_TEST_DATABASE");

    [Fact]
    public async Task Document_queries_translate_to_sql()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new Events());

        var document = new PlatformDocument
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = Guid.NewGuid(),
            Period = "2026-08",
            Platform = Platform.Bolt,
            DocumentType = PlatformDocumentType.CommissionInvoice,
            Status = PlatformDocumentStatus.Confirmed,
            PdfText = "Comision: 1.000,00",
        };
        var fields = new ExtractedFields(
            "Bolt Operations OÜ", "EE", "EE102090374", "X-1", new DateOnly(2026, 8, 31), null, null, "RON", 1000m, 1000m, []);

        IReadOnlyList<DocumentCheck> checks = await PlatformDocumentSupport.RunChecksAsync(db, document, fields, new AccountingOptions(), CancellationToken.None);
        checks.ShouldNotBeEmpty();

        (await PlatformDocumentSupport.LockReasonAsync(db, document, CancellationToken.None)).ShouldBeNull();
        (await PlatformDocumentSupport.IncludedInAsync(db, document.Id, CancellationToken.None)).ShouldBeEmpty();
        (await PlatformDocumentSupport.EnsureWritableAsync(db, document.PfaRegistrationId, document.Period, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await PlatformDocumentSupport.CurrentExtractionAsync(db, document.Id, CancellationToken.None)).ShouldBeNull();

        Result<IReadOnlyList<PlatformDocumentListItem>> list = await new ListPlatformDocumentsQueryHandler(db)
            .Handle(new ListPlatformDocumentsQuery(document.PfaRegistrationId, document.Period), CancellationToken.None);
        list.Value.ShouldBeEmpty();

        Result<PlatformDocumentDetail> missing = await new GetPlatformDocumentQueryHandler(db, Options.Create(new AccountingOptions()))
            .Handle(new GetPlatformDocumentQuery(document.Id), CancellationToken.None);
        missing.Error.ShouldBe(AccountingErrors.DocumentNotFound);
    }

    [Fact]
    public async Task Month_queries_translate_to_sql()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new Events());

        List<ScopePfa> scope = await AccountingScope.InPeriodAsync(db, "2026-08", CancellationToken.None);
        MonthData data = await MonthData.LoadAsync(db, "2026-08", [.. scope.Select(p => p.Id), Guid.NewGuid()], CancellationToken.None);
        data.Suppliers.ShouldNotBeEmpty();
        (await DeclarationSummaries.CurrentVersionsAsync(db, "2026-08", [Guid.NewGuid()], CancellationToken.None)).ShouldBeEmpty();

        Result<PeriodOverview> overview = await new GetPeriodOverviewQueryHandler(db, Options.Create(new AccountingOptions()))
            .Handle(new GetPeriodOverviewQuery("2026-08"), CancellationToken.None);
        overview.IsSuccess.ShouldBeTrue();
        (await PreCheck.RunAsync(db, Guid.NewGuid(), "2026-08", new AccountingOptions(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Declaration_queries_translate_to_sql()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new Events());
        var xml = new AnafDeclarationXmlService();
        var files = new DeclarationFiles(db, xml, new MemoryFiles(), new PlainSecrets());
        var validator = new DeclarationValidator(db, xml, new FakeAnafValidator(), files, Options.Create(new AccountingOptions()));
        var actions = new DeclarationActions(db, files, validator, Options.Create(new AccountingOptions()));
        var missing = Guid.NewGuid();

        (await DeclarationDtos.VersionsAsync(db, v => v.Declaration.Period == "2026-08", CancellationToken.None)).ShouldNotBeNull();
        (await new GetDeclarationQueryHandler(db).Handle(new GetDeclarationQuery(missing), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await new GetDeclarationBreakdownQueryHandler(db).Handle(new GetDeclarationBreakdownQuery(missing), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await new GetDeclarationValidationQueryHandler(db).Handle(new GetDeclarationValidationQuery(missing), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await new GetDeclarationFileQueryHandler(db, files).Handle(new GetDeclarationFileQuery(missing, DeclarationFileKind.Xml), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await new TransitionDeclarationVersionCommandHandler(db, new FixedUser(), actions)
            .Handle(new TransitionDeclarationVersionCommand(missing, DeclarationAction.Validate, null), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await validator.ValidateAsync(missing, null, CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await files.TaxpayerAsync(missing, CancellationToken.None)).ShouldBeNull();
        (await actions.CreateRectificationAsync(missing, "Corecție", null, CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await actions.UploadReceiptAsync(missing, new ReceiptFile("r.pdf", "application/pdf", [1]), null, null, CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await DeclarationContent.CalculateAsync(db, missing, "2026-08", DeclarationType.D301, Application.Accounting.Tax.TaxEngineSettings.From(new AccountingOptions()), CancellationToken.None))
            .IsFailure.ShouldBeTrue();
        (await new Application.Accounting.Audit.ListPfaAuditQueryHandler(db)
            .Handle(new Application.Accounting.Audit.ListPfaAuditQuery(missing, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "DeclarationVersion"), CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        // Schemele din migrația B4, alese după perioadă.
        List<AnafDeclarationSchema> schemas = await db.AnafDeclarationSchemas.AsNoTracking().ToListAsync();
        foreach (DeclarationType type in Enum.GetValues<DeclarationType>())
        {
            AnafDeclarationSchema schema = DeclarationFiles.PickSchema(schemas, type, "2026-08").ShouldNotBeNull();
            xml.Supports(type, schema.Version).ShouldBeTrue();
            AnafSchemas.Exists(schema.XsdPath!).ShouldBeTrue();
        }

        // Jobul de validare: selecția versiunilor GENERATED (subinterogarea cu Max) pe SQL.
        var job = new BackgroundJob
        {
            Id = Guid.NewGuid(),
            Type = BackgroundJobType.ValidateDeclarations,
            Status = BackgroundJobStatus.Queued,
            ParametersJson = "{\"period\":\"2026-08\"}",
            ResultJson = "{\"results\":[],\"errors\":[]}",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync();
        Result run = await new RunMonthJobCommandHandler(db, new NoExtraction(), files, validator, Options.Create(new AccountingOptions()))
            .Handle(new RunMonthJobCommand(job.Id), CancellationToken.None);
        run.IsSuccess.ShouldBeTrue();
        (await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id)).Status.ShouldBe(BackgroundJobStatus.Completed);
    }

    [Fact]
    public async Task Ledger_queries_translate_to_sql()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new Events());
        var pfaId = Guid.NewGuid();
        var options = new AccountingOptions();
        var context = new Application.Accounting.Ledger.LedgerImportContext(
            pfaId,
            Guid.NewGuid(),
            new DateOnly(2026, 1, 1),
            await Application.Accounting.Ledger.LedgerSupport.RulesAsync(db, pfaId, CancellationToken.None),
            await Application.Accounting.Ledger.LedgerSupport.ClosedPeriodsAsync(db, pfaId, CancellationToken.None),
            options);
        context.Rules.Categories.ShouldNotBeEmpty();

        (await new Application.Accounting.Ledger.BankLedgerSource(db).ImportAsync(context, CancellationToken.None)).Created.ShouldBe(0);
        (await new Application.Accounting.Ledger.PlatformLedgerSource(db).ImportAsync(context, CancellationToken.None)).Created.ShouldBe(0);
        (await new Application.Accounting.Ledger.ListLedgerQueryHandler(db)
            .Handle(new Application.Accounting.Ledger.ListLedgerQuery(pfaId, null, null, null, null, null), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await Application.Accounting.Ledger.LedgerSupport.DtosAsync(
            db.LedgerEntries.Where(Application.Accounting.Ledger.LedgerSupport.UndocumentedBankExpense(pfaId)).Where(e => e.Amount >= -300.01m && e.Amount <= -299.99m),
            CancellationToken.None)).ShouldBeEmpty();
        (await new Application.Accounting.Ledger.ListLedgerImportPfasQueryHandler(db)
            .Handle(new Application.Accounting.Ledger.ListLedgerImportPfasQuery("2026-10"), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Register_queries_translate_to_sql()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new Events());
        var pfaId = Guid.NewGuid();

        (await Application.Accounting.Registers.RegisterData.EntriesAsync(db, pfaId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None)).ShouldBeEmpty();
        (await Application.Accounting.Registers.RegisterData.PfaAsync(db, pfaId, CancellationToken.None)).ShouldBeNull();
        (await Application.Accounting.Assets.AssetSupport.DtosAsync(db, db.PfaAssets.Where(a => a.PfaRegistrationId == pfaId), new DateOnly(2026, 12, 31), CancellationToken.None)).ShouldBeEmpty();
        (await db.ExchangeRates.Where(r => new List<string> { "EUR" }.Contains(r.Currency) && r.Date >= new DateOnly(2026, 1, 1)).CountAsync()).ShouldBeGreaterThanOrEqualTo(0);
        (await db.ExpenseCategoryRules.AnyAsync(r => r.Category == Application.Accounting.Ledger.LedgerSupport.PlatformCommissionCategory)).ShouldBeTrue();
    }

    /// <summary>B8 pe Postgres: un PFA de test (baza e de unică folosință), perioade, sumar, dosar de predare.</summary>
    [Fact]
    public async Task Period_and_handover_queries_translate_to_sql()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new Events());
        var user = new Domain.Users.User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@test.ro", FirstName = "Test", LastName = "B8", PasswordHash = "x" };
        var pfa = new Domain.PfaRegistrations.PfaRegistration { Id = Guid.NewGuid(), UserId = user.Id, FullName = "Test B8", Cui = "12345674" };
        db.Users.Add(user);
        db.PfaRegistrations.Add(pfa);
        db.PfaAccountingEngagements.Add(new PfaAccountingEngagement { Id = Guid.NewGuid(), PfaRegistrationId = pfa.Id, StartDate = new DateOnly(2026, 1, 1), Status = EngagementStatus.Active });
        // Închiderea cere controalele lunii trecute (spec flux contabil §8): banca și e-Factura la zi.
        var connection = new Domain.Banking.BankConnection
        {
            Id = Guid.NewGuid(), UserId = user.Id, Provider = "test", InstitutionId = "BT", ProviderConsentId = $"c-{Guid.NewGuid():N}",
            Reference = Guid.NewGuid().ToString("N"), Status = Domain.Banking.BankConnectionStatus.Linked, LastSyncedAtUtc = DateTime.UtcNow,
        };
        db.BankConnections.Add(connection);
        db.AnafPfaLinks.Add(new AnafPfaLink { Id = Guid.NewGuid(), PfaRegistrationId = pfa.Id, Status = AnafPfaLinkStatus.Active, EnabledByUserId = user.Id, LastSyncAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        (await new Application.Accounting.Periods.GetMonthReconciliationQueryHandler(db)
            .Handle(new Application.Accounting.Periods.GetMonthReconciliationQuery(pfa.Id, "2026-02"), CancellationToken.None)).Value.CanClose.ShouldBeTrue();
        (await new Application.Accounting.Periods.ListPeriodsQueryHandler(db).Handle(new Application.Accounting.Periods.ListPeriodsQuery(pfa.Id), CancellationToken.None))
            .Value.Count.ShouldBeGreaterThan(7);
        (await new Application.Accounting.Periods.ClosePeriodCommandHandler(db, new UserOf(user.Id))
            .Handle(new Application.Accounting.Periods.ClosePeriodCommand(pfa.Id, "2026-02"), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await new Application.Accounting.Pfas.GetPfaSummaryQueryHandler(db).Handle(new Application.Accounting.Pfas.GetPfaSummaryQuery(pfa.Id), CancellationToken.None))
            .Value.Cui.ShouldBe("12345674");

        var exporter = new Infrastructure.Accounting.RegisterExporter();
        var files = new DeclarationFiles(db, new AnafDeclarationXmlService(), new MemoryFiles(), new PlainSecrets());
        var refQuery = new Application.Accounting.FiscalRegister.GetRefQueryHandler(db);
        JobRef job = (await new Application.Accounting.Handover.StartHandoverPackageCommandHandler(db, new UserOf(user.Id))
            .Handle(new Application.Accounting.Handover.StartHandoverPackageCommand(pfa.Id), CancellationToken.None)).Value;
        (await new Application.Accounting.Handover.RunHandoverPackageCommandHandler(
                db,
                files,
                exporter,
                refQuery,
                new Application.Accounting.Registers.ExportRjipQueryHandler(db, new Application.Accounting.Registers.GetRjipQueryHandler(db), exporter),
                new Application.Accounting.FiscalRegister.ExportRefQueryHandler(db, refQuery, exporter),
                new Application.Accounting.Registers.ExportInventoryQueryHandler(db, exporter))
            .Handle(new Application.Accounting.Handover.RunHandoverPackageCommand(job.JobId), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.Id == job.JobId)).Status.ShouldBe(BackgroundJobStatus.Completed);
    }

    [Fact]
    public async Task List_settings_and_rule_queries_translate_to_sql()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new Events());

        IReadOnlyList<PfaListItem> pfas = (await new Application.Accounting.Pfas.ListPfasQueryHandler(db)
            .Handle(new Application.Accounting.Pfas.ListPfasQuery("active", "test"), CancellationToken.None)).Value;
        pfas.ShouldNotBeNull();
        (await new Application.Accounting.Pfas.GetPfaSettingsQueryHandler(db)
            .Handle(new Application.Accounting.Pfas.GetPfaSettingsQuery(Guid.NewGuid()), CancellationToken.None)).IsFailure.ShouldBeTrue();
        foreach (Application.Accounting.Rules.TaxRuleKind kind in Enum.GetValues<Application.Accounting.Rules.TaxRuleKind>())
        {
            (await new Application.Accounting.Rules.ListTaxRulesQueryHandler(db)
                .Handle(new Application.Accounting.Rules.ListTaxRulesQuery(kind), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        (await new Application.Accounting.Rules.GetExchangeRateQueryHandler(db)
            .Handle(new Application.Accounting.Rules.GetExchangeRateQuery("EUR", new DateOnly(2026, 8, 31)), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await new Application.Accounting.Pfas.GetMyCashPreferenceQueryHandler(db, new UserOf(Guid.NewGuid()))
            .Handle(new Application.Accounting.Pfas.GetMyCashPreferenceQuery(), CancellationToken.None)).IsFailure.ShouldBeTrue();

        // Cererile D700: ultima cerere per PFA (subinterogare corelată) și cererea unui PFA.
        (await new Application.Accounting.VatRegistration.ListVatRegistrationsQueryHandler(db, new UserOf(Guid.NewGuid()))
            .Handle(new Application.Accounting.VatRegistration.ListVatRegistrationsQuery(), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await new Application.Accounting.VatRegistration.GetPfaVatRegistrationQueryHandler(db)
            .Handle(new Application.Accounting.VatRegistration.GetPfaVatRegistrationQuery(Guid.NewGuid()), CancellationToken.None)).Value.ShouldBeNull();

        // e-Factura: lista mesajelor unui PFA (unul care nu există → PfaNotFound) și conexiunea ANAF.
        var anaf = new Application.Accounting.Anaf.AnafEFacturaService(
            db, new NoAnaf(), new PlainSecrets(), new DeclarationFiles(db, new AnafDeclarationXmlService(), new MemoryFiles(), new PlainSecrets()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Application.Accounting.Anaf.AnafEFacturaService>.Instance);
        (await new Application.Accounting.Anaf.GetPfaEFacturaQueryHandler(db, anaf)
            .Handle(new Application.Accounting.Anaf.GetPfaEFacturaQuery(Guid.NewGuid()), CancellationToken.None)).IsFailure.ShouldBeTrue();
        (await new Application.Accounting.Anaf.GetAnafConnectionQueryHandler(db, anaf)
            .Handle(new Application.Accounting.Anaf.GetAnafConnectionQuery(), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        if (await db.PfaRegistrations.Select(p => (Guid?)p.Id).FirstOrDefaultAsync() is { } anyPfa)
        {
            (await new Application.Accounting.Anaf.GetPfaEFacturaQueryHandler(db, anaf)
                .Handle(new Application.Accounting.Anaf.GetPfaEFacturaQuery(anyPfa), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }
    }

    /// <summary>ANAF neconfigurat: interogările nu ajung la el.</summary>
    private sealed class NoAnaf : Application.Abstractions.Anaf.IAnafEFacturaClient
    {
        public bool IsConfigured => false;

        public Uri AuthorizeUrl(string state) => new("https://logincert.anaf.ro/");

        public Task<Result<Application.Abstractions.Anaf.AnafTokens>> ExchangeCodeAsync(string code, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<Application.Abstractions.Anaf.AnafTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<Application.Abstractions.Anaf.EFacturaPage>> ListMessagesAsync(string accessToken, string cif, DateTime fromUtc, DateTime toUtc, int page, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<byte[]>> DownloadAsync(string accessToken, string messageId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<byte[]>> ToPdfAsync(byte[] invoiceXml, bool creditNote, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UserOf(Guid id) : Application.Abstractions.Authentication.IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class FixedUser : Application.Abstractions.Authentication.IUserContext
    {
        public Guid UserId => Guid.Empty;
    }

    private sealed class NoExtraction : Application.Abstractions.Messaging.ICommandHandler<RunPlatformDocumentExtractionCommand>
    {
        public Task<Result> Handle(RunPlatformDocumentExtractionCommand command, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
