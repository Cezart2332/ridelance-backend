using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Application.Accounting.Months;
using Domain.Accounting;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// B3: pre-check-ul și procesarea lunii, pe o variantă mică a fixtures din frontend — Ion gata,
/// Ana cu documente de confirmat, Bogdan de verificat, George fără factura Uber, plus un PFA
/// care nu intră în lună.
/// </summary>
public sealed class MonthProcessingTests : IDisposable
{
    private const string Period = "2026-08";

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly IOptions<AccountingOptions> _options = Options.Create(new AccountingOptions());
    private readonly Guid _accountant = Guid.NewGuid();
    private readonly Guid _ion;
    private readonly Guid _ana;
    private readonly Guid _bogdan;
    private readonly Guid _george;

    public MonthProcessingTests()
    {
        _db.Users.Add(new User { Id = _accountant, Email = "contabil@ridelance.ro", FirstName = "Contabil", LastName = "RIDElance", Role = UserRole.Contabil });
        _ion = Pfa("Ion Popescu");
        _ana = Pfa("Ana Georgescu");
        _bogdan = Pfa("Bogdan Matei");
        _george = Pfa("George Stan");
        // Onboarding neîncheiat: nu intră în lună.
        Pfa("Nou Înscris", onboarded: false);
        Rules();

        Invoice(_ion, Platform.Bolt, 1000m);
        Report(_ion, Platform.Bolt, 8000m, 1000m);
        Invoice(_ion, Platform.Uber, 600m);
        Report(_ion, Platform.Uber, 5000m, 600m);

        Invoice(_ana, Platform.Bolt, 900m, PlatformDocumentStatus.PendingConfirmation);
        Report(_ana, Platform.Bolt, 6000m, 900m, PlatformDocumentStatus.PendingConfirmation);
        Invoice(_ana, Platform.Uber, 500m, PlatformDocumentStatus.PendingConfirmation);
        Report(_ana, Platform.Uber, 3500m, 500m, PlatformDocumentStatus.PendingConfirmation);

        Invoice(_bogdan, Platform.Bolt, 1284.50m, PlatformDocumentStatus.NeedsReview,
            new DocumentCheck(DocumentCheckCode.AmountInText, false, "Suma 1.284,50 (comision) nu apare în textul documentului.", null));
        Report(_bogdan, Platform.Bolt, 7900m, 1248.50m);
        Invoice(_bogdan, Platform.Uber, 714m);
        Report(_bogdan, Platform.Uber, 4200m, 714m);

        Invoice(_george, Platform.Bolt, 1065m);
        Report(_george, Platform.Bolt, 7100m, 1065m);
        Report(_george, Platform.Uber, 3900m, 663m);

        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Month_goes_from_not_processed_to_generated_declarations()
    {
        PeriodOverview before = await Overview();
        before.Stats.ShouldBe(new PeriodStats(4, 0, 0, 0, 4));
        before.Rows.Select(r => r.PfaName).ShouldNotContain("Nou Înscris");

        JobDto processed = await RunJob(BackgroundJobType.ProcessPeriod);
        processed.Status.ShouldBe(BackgroundJobStatus.Completed);
        processed.Progress.ShouldBe(new JobProgress(4, 4));
        processed.Results.Count.ShouldBe(4);

        PeriodOverview afterProcess = await Overview();
        afterProcess.Stats.ShouldBe(new PeriodStats(4, 1, 2, 1, 0));
        Row(afterProcess, _ana).BlockingReasons.ShouldBe(["4 documente așteaptă confirmare."]);
        string bogdan = Row(afterProcess, _bogdan).BlockingReasons[0];
        bogdan.ShouldStartWith("Factura Bolt EE-");
        bogdan.ShouldEndWith(": Suma 1.284,50 (comision) nu apare în textul documentului.");
        Row(afterProcess, _george).BlockingReasons.ShouldBe(["Lipsește factura de comision Uber."]);

        ConfirmBulkResult confirmed = (await new ConfirmCleanDocumentsCommandHandler(_db, User(), _options)
            .Handle(new ConfirmCleanDocumentsCommand(Period), CancellationToken.None)).Value;
        confirmed.Confirmed.Count.ShouldBe(4);

        PeriodOverview ready = await Overview();
        ready.Stats.ShouldBe(new PeriodStats(4, 2, 1, 1, 0));
        OverviewRow ion = Row(ready, _ion);
        ion.Declarations[DeclarationType.D100].ShouldBe(new DeclarationCell(null, null, DeclarationStatus.Draft, 20m));
        ion.Declarations[DeclarationType.D301].Amount.ShouldBe(336m);
        ion.Declarations[DeclarationType.D390].ShouldBe(new DeclarationCell(null, null, DeclarationStatus.Draft, 0m));
        ion.Bolt.ShouldBe(new PlatformMonthFigures(8000m, 1000m));
        Row(ready, _george).Declarations[DeclarationType.D100].Status.ShouldBe(DeclarationStatus.BlockedMissingDocuments);

        JobDto generated = await RunJob(BackgroundJobType.GenerateDeclarations);
        generated.Progress.ShouldBe(new JobProgress(2, 2));
        generated.Errors.ShouldBeEmpty();

        List<DeclarationVersion> versions = await _db.DeclarationVersions.Include(v => v.Declaration).Include(v => v.Lines)
            .Where(v => v.Declaration.PfaRegistrationId == _ion).ToListAsync();
        versions.Count.ShouldBe(3);
        versions.ShouldAllBe(v => v.VersionNo == 1 && v.Kind == DeclarationVersionKind.Initial && v.Status == DeclarationStatus.Generated);
        versions.Single(v => v.Declaration.Type == DeclarationType.D100).Amount.ShouldBe(20m);
        versions.Single(v => v.Declaration.Type == DeclarationType.D301).Amount.ShouldBe(336m);
        DeclarationVersion d390 = versions.Single(v => v.Declaration.Type == DeclarationType.D390);
        d390.Amount.ShouldBe(0m);
        d390.Lines.Select(l => (l.OperationType, l.SupplierCountry, l.Base)).ShouldBe([("S", "EE", 1000m), ("S", "NL", 600m)], ignoreOrder: true);
        versions.Single(v => v.Declaration.Type == DeclarationType.D301).SnapshotJson.ShouldContain("\"invoices\"");
        (await _db.AuditLogs.CountAsync(a => a.Action == "GENERATE")).ShouldBe(6);

        // Idempotență: a doua generare nu mai are ce face.
        JobDto again = await RunJob(BackgroundJobType.GenerateDeclarations);
        again.Progress.ShouldBe(new JobProgress(0, 0));
        (await _db.DeclarationVersions.CountAsync()).ShouldBe(6);

        IReadOnlyList<DeclarationSummary> ionDeclarations = await Declarations(_ion);
        ionDeclarations.Select(d => (d.Type, d.Status, d.Amount)).ShouldBe(
        [
            (DeclarationType.D100, (DeclarationStatus?)DeclarationStatus.Generated, (decimal?)20m),
            (DeclarationType.D301, DeclarationStatus.Generated, 336m),
            (DeclarationType.D390, DeclarationStatus.Generated, 0m),
        ]);
        (await Declarations(_george)).ShouldAllBe(d => d.Status == DeclarationStatus.BlockedMissingDocuments);
    }

    [Fact]
    public async Task Fixing_a_document_after_processing_refreshes_the_month()
    {
        await RunJob(BackgroundJobType.ProcessPeriod);
        PlatformDocument bogdanInvoice = await _db.PlatformDocuments.SingleAsync(d => d.PfaRegistrationId == _bogdan && d.Status == PlatformDocumentStatus.NeedsReview);
        bogdanInvoice.Status = PlatformDocumentStatus.Confirmed;
        await _db.SaveChangesAsync();

        await PreCheck.RefreshIfProcessedAsync(_db, _bogdan, Period, _options.Value, CancellationToken.None);

        Row(await Overview(), _bogdan).Status.ShouldBe(PfaMonthStatus.Ready);
    }

    [Fact]
    public async Task Missing_art317_setting_needs_review()
    {
        await _db.PfaAccountingSettings.Where(s => s.PfaRegistrationId == _ion && s.Key == PfaAccountingSettingKeys.Art317).LoadAsync();
        PfaAccountingSetting art317 = _db.PfaAccountingSettings.Local.Single(s => s.PfaRegistrationId == _ion && s.Key == PfaAccountingSettingKeys.Art317);
        _db.PfaAccountingSettings.Add(new PfaAccountingSetting
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = _ion,
            Key = PfaAccountingSettingKeys.Art317,
            ValueJson = "false",
            ValidFrom = new DateOnly(2026, 8, 1),
            Note = "Cod retras",
            ChangedByUserId = _accountant,
        });
        await _db.SaveChangesAsync();
        art317.ValidFrom.ShouldBe(new DateOnly(2026, 1, 1));

        PreCheckResult? result = await PreCheck.RunAsync(_db, _ion, Period, _options.Value, CancellationToken.None);

        result!.Status.ShouldBe(PfaMonthStatus.NeedsReview);
        result.Reasons.ShouldContain("Codul special de TVA art. 317 nu e activ în perioadă (Setări contabilitate).");
    }

    [Fact]
    public async Task Inactive_engagement_before_the_month_excludes_the_pfa()
    {
        _db.PfaAccountingEngagements.Add(new PfaAccountingEngagement
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = _george,
            StartDate = new DateOnly(2025, 1, 1),
            EndDate = new DateOnly(2026, 6, 30),
            Status = EngagementStatus.Inactive,
        });
        await _db.SaveChangesAsync();

        (await Overview()).Rows.Select(r => r.PfaId).ShouldNotContain(_george);
    }

    // ─── Ajutoare ──────────────────────────────────────────────────────────────────────────────

    private async Task<JobDto> RunJob(BackgroundJobType type)
    {
        JobRef started = (await new StartMonthJobCommandHandler(_db, User()).Handle(new StartMonthJobCommand(type, Period), CancellationToken.None)).Value;
        Result run = await new RunMonthJobCommandHandler(_db, new NoExtraction(), _options).Handle(new RunMonthJobCommand(started.JobId), CancellationToken.None);
        run.IsSuccess.ShouldBeTrue();
        return (await new GetJobQueryHandler(_db).Handle(new GetJobQuery(started.JobId), CancellationToken.None)).Value;
    }

    private async Task<PeriodOverview> Overview() =>
        (await new GetPeriodOverviewQueryHandler(_db, _options).Handle(new GetPeriodOverviewQuery(Period), CancellationToken.None)).Value;

    private async Task<IReadOnlyList<DeclarationSummary>> Declarations(Guid pfaId) =>
        (await new ListDeclarationsQueryHandler(_db, _options).Handle(new ListDeclarationsQuery(pfaId, Period), CancellationToken.None)).Value;

    private static OverviewRow Row(PeriodOverview overview, Guid pfaId) => overview.Rows.Single(r => r.PfaId == pfaId);

    private Guid Pfa(string name, bool onboarded = true)
    {
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@ridelance.ro", FirstName = name, Role = UserRole.Client };
        var pfa = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            FullName = name,
            Cui = "41000001",
            OnboardingCompletedAtUtc = onboarded ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
        };
        _db.PfaRegistrations.Add(pfa);
        _db.PfaAccountingSettings.Add(Setting(pfa.Id, PfaAccountingSettingKeys.Art317, "true"));
        _db.PfaAccountingSettings.Add(Setting(pfa.Id, PfaAccountingSettingKeys.Platforms, "[\"BOLT\",\"UBER\"]"));
        return pfa.Id;
    }

    private PfaAccountingSetting Setting(Guid pfaId, string key, string value) => new()
    {
        Id = Guid.NewGuid(),
        PfaRegistrationId = pfaId,
        Key = key,
        ValueJson = value,
        ValidFrom = new DateOnly(2026, 1, 1),
        Note = "Setare inițială",
        ChangedByUserId = _accountant,
    };

    private void Rules()
    {
        _db.SupplierTaxProfiles.AddRange(
            new SupplierTaxProfile
            {
                Id = Guid.NewGuid(), SupplierName = "Bolt Operations OÜ", Country = "EE", VatId = "EE102090374", D100Rate = 2, D100RateConfirmed = true,
                ValidFrom = new DateOnly(2025, 1, 1), ResidenceCertValidFrom = new DateOnly(2026, 1, 1), ResidenceCertValidTo = new DateOnly(2026, 12, 31),
            },
            new SupplierTaxProfile
            {
                Id = Guid.NewGuid(), SupplierName = "Uber B.V.", Country = "NL", VatId = "NL852071588B01", D100Rate = 0, D100RateConfirmed = true,
                ValidFrom = new DateOnly(2025, 1, 1), ResidenceCertValidFrom = new DateOnly(2026, 1, 1), ResidenceCertValidTo = new DateOnly(2026, 12, 31),
            });
        _db.VatRates.Add(new VatRate { Id = Guid.NewGuid(), Rate = 21, ValidFrom = new DateOnly(2025, 8, 1) });
        _db.D100Rules.Add(new D100Rule { Id = Guid.NewGuid(), Code = D100RuleCode.D100CommissionNonresident, Enabled = true, ValidFrom = new DateOnly(2025, 1, 1) });
    }

    private int _invoiceNumber;

    private void Invoice(Guid pfaId, Platform platform, decimal commission, PlatformDocumentStatus status = PlatformDocumentStatus.Confirmed, params DocumentCheck[] failed)
    {
        bool bolt = platform == Platform.Bolt;
        Add(pfaId, platform, PlatformDocumentType.CommissionInvoice, status, new DocumentExtraction
        {
            SupplierName = bolt ? "Bolt Operations OÜ" : "Uber B.V.",
            SupplierCountry = bolt ? "EE" : "NL",
            SupplierVatId = bolt ? "EE102090374" : "NL852071588B01",
            InvoiceNumber = $"{(bolt ? "EE" : "UBR")}-{++_invoiceNumber}",
            InvoiceDate = new DateOnly(2026, 8, 31),
            PeriodFrom = new DateOnly(2026, 8, 1),
            PeriodTo = new DateOnly(2026, 8, 31),
            Currency = "RON",
            Amount = commission,
            CommissionAmount = commission,
            ChecksResultJson = AccountingJson.Serialize(failed),
        });
    }

    private void Report(Guid pfaId, Platform platform, decimal income, decimal commission, PlatformDocumentStatus status = PlatformDocumentStatus.Confirmed) =>
        Add(pfaId, platform, PlatformDocumentType.PlatformReport, status, new DocumentExtraction
        {
            SupplierName = platform == Platform.Bolt ? "Bolt Operations OÜ" : "Uber B.V.",
            InvoiceDate = new DateOnly(2026, 9, 1),
            PeriodFrom = new DateOnly(2026, 8, 1),
            PeriodTo = new DateOnly(2026, 8, 31),
            Currency = "RON",
            Amount = income,
            CommissionAmount = commission,
        });

    private void Add(Guid pfaId, Platform platform, PlatformDocumentType type, PlatformDocumentStatus status, DocumentExtraction extraction)
    {
        var file = new Document { Id = Guid.NewGuid(), OriginalFileName = $"{platform}-{type}.pdf", ContentType = "application/pdf", Origin = DocumentOrigin.AccountingUpload };
        var document = new PlatformDocument
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfaId,
            Period = Period,
            Platform = platform,
            DocumentType = type,
            SourceDocumentId = file.Id,
            FileHash = Guid.NewGuid().ToString("N"),
            Status = status,
            // Textul PDF conține sumele citite, ca verificarea „sumele apar în text” să treacă.
            PdfText = $"{AccountingJson.Amount(extraction.Amount ?? 0)} {AccountingJson.Amount(extraction.CommissionAmount ?? 0)}",
        };
        extraction.Id = Guid.NewGuid();
        extraction.PlatformDocumentId = document.Id;
        extraction.Version = 1;
        extraction.IsCurrent = true;
        _db.Documents.Add(file);
        _db.PlatformDocuments.Add(document);
        _db.DocumentExtractions.Add(extraction);
    }

    private FixedUser User() => new(_accountant);

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class NoExtraction : ICommandHandler<RunPlatformDocumentExtractionCommand>
    {
        public Task<Result> Handle(RunPlatformDocumentExtractionCommand command, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
