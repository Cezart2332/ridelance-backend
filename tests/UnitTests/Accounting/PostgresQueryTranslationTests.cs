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
