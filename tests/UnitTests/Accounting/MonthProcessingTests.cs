using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.Documents;
using Application.Accounting.Months;
using Domain.Accounting;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
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
    private readonly MemoryFiles _files = new();
    private readonly FakeAnafValidator _anaf = new();
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

    // ─── B4: XML și validarea pe 3 niveluri ───────────────────────────────────────────────────

    [Fact]
    public async Task Generation_writes_the_xml_of_each_version()
    {
        await GenerateIon();

        List<DeclarationVersion> versions = await IonVersions();
        versions.ShouldAllBe(v => v.XmlDocumentId != null && v.SchemaId != null);
        Guid? xmlId = versions.Single(v => v.Declaration.Type == DeclarationType.D301).XmlDocumentId;
        Domain.Documents.Document xml = await _db.Documents.SingleAsync(d => d.Id == xmlId);
        (xml.OriginalFileName, xml.ContentType, xml.Origin).ShouldBe(("D301_12345674_2026-08_v1.xml", "application/xml", Domain.Documents.DocumentOrigin.AccountingGenerated));
        xml.IsUserFacing.ShouldBeFalse();

        string content = System.Text.Encoding.UTF8.GetString(_files.Files[xml.StoredFileName]);
        content.ShouldContain("tva4=\"336\"");
        content.ShouldContain("cont=\"RO49AAAA1B31007593840000\"");
        // IBAN-ul stă doar în XML-ul criptat, nu în snapshot-ul din bază.
        versions.ShouldAllBe(v => !v.SnapshotJson.Contains("RO49AAAA1B31007593840000"));
        versions.Single(v => v.Declaration.Type == DeclarationType.D301).SnapshotJson.ShouldContain("\"taxpayer\"");
    }

    [Fact]
    public async Task Validation_job_makes_the_declarations_ready_to_sign()
    {
        await GenerateIon();

        JobDto validated = await RunJob(BackgroundJobType.ValidateDeclarations);

        validated.Progress.ShouldBe(new JobProgress(2, 2));
        validated.Errors.ShouldBeEmpty();
        _anaf.Calls.Where(call => call.Version == "2026-09").Select(call => call.Type).Distinct().ShouldBe([DeclarationType.D100, DeclarationType.D301, DeclarationType.D390], ignoreOrder: true);

        List<DeclarationVersion> versions = await IonVersions();
        versions.ShouldAllBe(v => v.Status == DeclarationStatus.ReadyToSign && v.PdfDocumentId != null);
        DeclarationVersion d100 = versions.Single(v => v.Declaration.Type == DeclarationType.D100);
        AccountingJson.Deserialize<List<StatusHistoryRecord>>(d100.StatusHistoryJson, []).Select(h => h.To)
            .ShouldBe([DeclarationStatus.Generated, DeclarationStatus.Validated, DeclarationStatus.ReadyToSign]);

        ValidationResult result = (await new GetDeclarationValidationQueryHandler(_db).Handle(new GetDeclarationValidationQuery(d100.Id), CancellationToken.None)).Value!;
        result.Levels.Select(l => (l.Level, l.Passed)).ShouldBe([(ValidationLevel.Ridelance, true), (ValidationLevel.Xsd, true), (ValidationLevel.Anaf, true)]);
        result.ValidatorVersion.ShouldBe("2026-09");

        DeclarationFile pdf = (await new GetDeclarationFileQueryHandler(_db, Files()).Handle(new GetDeclarationFileQuery(d100.Id, DeclarationFileKind.Pdf), CancellationToken.None)).Value;
        (pdf.FileName, pdf.ContentType).ShouldBe(("D100_12345674_2026-08_v1.pdf", "application/pdf"));
        pdf.Content.ShouldBe(FakeAnafValidator.Pdf);

        // Documentele sursă sunt blocate de la VALIDATED (Decizii pct. 3).
        PlatformDocument invoice = await _db.PlatformDocuments.FirstAsync(d => d.PfaRegistrationId == _ion && d.DocumentType == PlatformDocumentType.CommissionInvoice);
        (await PlatformDocumentSupport.LockReasonAsync(_db, invoice, CancellationToken.None)).ShouldNotBeNull();

        // A doua validare nu mai are ce face.
        (await RunJob(BackgroundJobType.ValidateDeclarations)).Progress.ShouldBe(new JobProgress(0, 0));
    }

    [Fact]
    public async Task Anaf_errors_fail_the_declaration_with_their_messages()
    {
        await GenerateIon();
        _anaf.Respond = (type, _) => type == DeclarationType.D390
            ? new Application.Abstractions.Anaf.AnafValidatorResult(
                false,
                [new Application.Abstractions.Anaf.AnafValidatorMessage("R24.1", "codO invalid (nu respecta algoritmul de tara)", "codO", "operatie (2)")],
                [],
                "E: operatie (2)",
                null,
                1300,
                "test")
            : new Application.Abstractions.Anaf.AnafValidatorResult(true, [], [], "ok", FakeAnafValidator.Pdf, 1500, "test");

        JobDto validated = await RunJob(BackgroundJobType.ValidateDeclarations);

        // Ion și Ana: D100 și D301 trec, D390 pică la ANAF.
        validated.Errors.Count.ShouldBe(2);
        validated.Errors.Single(e => e.PfaId == _ion).Message.ShouldBe(
            "D100: pregătit pentru depunere. D301: pregătit pentru depunere. D390: R24.1: codO invalid (nu respecta algoritmul de tara) (operatie (2))");
        DeclarationVersion d390 = (await IonVersions()).Single(v => v.Declaration.Type == DeclarationType.D390);
        d390.Status.ShouldBe(DeclarationStatus.ValidationFailed);
        d390.PdfDocumentId.ShouldBeNull();
        ValidationResult result = (await new GetDeclarationValidationQueryHandler(_db).Handle(new GetDeclarationValidationQuery(d390.Id), CancellationToken.None)).Value!;
        ValidationMessage message = result.Levels.Single(l => l.Level == ValidationLevel.Anaf).Messages.ShouldHaveSingleItem();
        (message.Field, message.Text).ShouldBe(("codO", "R24.1: codO invalid (nu respecta algoritmul de tara) (operatie (2))"));
        d390.ValidationResultJson!.ShouldContain("E: operatie (2)");
    }

    [Fact]
    public async Task Unavailable_anaf_validator_keeps_the_declaration_generated()
    {
        await GenerateIon();
        _anaf.Respond = (_, _) => Result.Failure<Application.Abstractions.Anaf.AnafValidatorResult>(
            Error.Problem("Accounting.AnafValidatorUnavailable", "Validatorul ANAF: serviciul nu răspunde."));

        await RunJob(BackgroundJobType.ValidateDeclarations);

        List<DeclarationVersion> versions = await IonVersions();
        versions.ShouldAllBe(v => v.Status == DeclarationStatus.Generated);
        ValidationResult result = (await new GetDeclarationValidationQueryHandler(_db).Handle(new GetDeclarationValidationQuery(versions[0].Id), CancellationToken.None)).Value!;
        result.Levels.Single(l => l.Level == ValidationLevel.Anaf).Messages[0].Text.ShouldStartWith("Validatorul ANAF nu a putut fi folosit: Validatorul ANAF: serviciul nu răspunde.");
    }

    [Fact]
    public async Task Missing_bank_account_fails_d301_before_calling_anaf()
    {
        await GenerateIon();
        _db.PfaBankAccountDeclarations.RemoveRange(_db.PfaBankAccountDeclarations);
        await _db.SaveChangesAsync();

        await RunJob(BackgroundJobType.ValidateDeclarations);

        DeclarationVersion d301 = (await IonVersions()).Single(v => v.Declaration.Type == DeclarationType.D301);
        d301.Status.ShouldBe(DeclarationStatus.ValidationFailed);
        _anaf.Calls.ShouldNotContain(call => call.Type == DeclarationType.D301);
        ValidationResult result = (await new GetDeclarationValidationQueryHandler(_db).Handle(new GetDeclarationValidationQuery(d301.Id), CancellationToken.None)).Value!;
        result.Levels[0].Messages.Select(m => m.Field).ShouldBe(["banca", "cont"]);
        result.Levels[2].Messages[0].Text.ShouldBe("Nu s-a rulat: nivelurile anterioare au erori.");
    }

    [Fact]
    public async Task Changed_snapshot_fails_level_one()
    {
        await GenerateIon();
        DeclarationVersion d100 = (await IonVersions()).Single(v => v.Declaration.Type == DeclarationType.D100);
        d100.Amount = 25m;
        await _db.SaveChangesAsync();

        Result<ValidationRun> run = await Validator().ValidateAsync(d100.Id, _accountant, CancellationToken.None);

        run.Value.Status.ShouldBe(DeclarationStatus.ValidationFailed);
        ValidationResult result = (await new GetDeclarationValidationQueryHandler(_db).Handle(new GetDeclarationValidationQuery(d100.Id), CancellationToken.None)).Value!;
        result.Levels[0].Messages.Select(m => m.Field).ShouldContain("total");
        result.Levels[0].Messages.Select(m => m.Field).ShouldContain("suma_dat");
    }

    [Fact]
    public async Task Validate_transition_returns_the_version_and_refuses_a_second_validation()
    {
        await GenerateIon();
        DeclarationVersion d301 = (await IonVersions()).Single(v => v.Declaration.Type == DeclarationType.D301);
        var handler = new TransitionDeclarationVersionCommandHandler(_db, User(), Actions());

        DeclarationVersionDto dto = (await handler.Handle(new TransitionDeclarationVersionCommand(d301.Id, DeclarationAction.Validate, null), CancellationToken.None)).Value;

        (dto.Status, dto.HasXml, dto.HasPdf, dto.SchemaVersion).ShouldBe((DeclarationStatus.ReadyToSign, true, true, "v1-20200130"));
        dto.StatusHistory.Select(h => (h.To, h.By.Name)).ShouldBe(
        [
            (DeclarationStatus.Generated, "Contabil RIDElance"),
            (DeclarationStatus.Validated, "Contabil RIDElance"),
            (DeclarationStatus.ReadyToSign, "Contabil RIDElance"),
        ]);

        Result<DeclarationVersionDto> again = await handler.Handle(new TransitionDeclarationVersionCommand(d301.Id, DeclarationAction.Validate, null), CancellationToken.None);
        again.Error.Code.ShouldBe("Accounting.InvalidTransition");
        again.Error.Description.ShouldBe("Acțiunea VALIDATE nu e permisă din statusul READY_TO_SIGN.");
    }

    [Fact]
    public async Task Declaration_detail_breakdown_and_xml_are_readable()
    {
        await GenerateIon();
        DeclarationVersion d301 = (await IonVersions()).Single(v => v.Declaration.Type == DeclarationType.D301);

        DeclarationDetail detail = (await new GetDeclarationQueryHandler(_db).Handle(new GetDeclarationQuery(d301.DeclarationId), CancellationToken.None)).Value;
        (detail.Type, detail.Period, detail.CurrentVersionId, detail.Versions.Count).ShouldBe((DeclarationType.D301, Period, d301.Id, 1));

        DeclarationBreakdown breakdown = (await new GetDeclarationBreakdownQueryHandler(_db).Handle(new GetDeclarationBreakdownQuery(d301.Id), CancellationToken.None)).Value;
        (breakdown.Total, breakdown.ExcludedRideIncome).ShouldBe((336m, 13000m));
        breakdown.Lines.Select(l => (l.SourceDocumentLabel.StartsWith("Factura ", StringComparison.Ordinal), l.Base, l.Value)).ShouldBe([(true, 1000m, 210m), (true, 600m, 126m)]);

        DeclarationFile xml = (await new GetDeclarationFileQueryHandler(_db, Files()).Handle(new GetDeclarationFileQuery(d301.Id, DeclarationFileKind.Xml), CancellationToken.None)).Value;
        xml.ContentType.ShouldBe("application/xml");
        (await new GetDeclarationValidationQueryHandler(_db).Handle(new GetDeclarationValidationQuery(d301.Id), CancellationToken.None)).Value.ShouldBeNull();
        (await new GetDeclarationFileQueryHandler(_db, Files()).Handle(new GetDeclarationFileQuery(d301.Id, DeclarationFileKind.Pdf), CancellationToken.None))
            .Error.Code.ShouldBe("Accounting.FileMissing");
    }

    // ─── B5: statusuri, recipisă, rectificative ───────────────────────────────────────────────

    [Fact]
    public async Task Declaration_goes_from_generated_to_accepted()
    {
        await GenerateIon();
        Guid d301 = await IonVersion(DeclarationType.D301);

        (await Transition(d301, DeclarationAction.Validate)).Value.Status.ShouldBe(DeclarationStatus.ReadyToSign);
        (await Transition(d301, DeclarationAction.MarkSigned)).Value.Status.ShouldBe(DeclarationStatus.Signed);
        (await Transition(d301, DeclarationAction.MarkSubmitted, "Depus prin SPV")).Value.Status.ShouldBe(DeclarationStatus.Submitted);
        DeclarationVersionDto accepted = (await Receipt(d301, "INTERNT-123")).Value;

        (accepted.Status, accepted.ReceiptNumber, accepted.ReceiptFile!.FileName).ShouldBe((DeclarationStatus.Accepted, "INTERNT-123", "recipisa.pdf"));
        accepted.StatusHistory.Select(h => (h.To, h.Note)).ShouldBe(
        [
            (DeclarationStatus.Generated, null),
            (DeclarationStatus.Validated, "Validare RIDElance, XSD și ANAF trecută."),
            (DeclarationStatus.ReadyToSign, "PDF generat de DUKIntegrator."),
            (DeclarationStatus.Signed, null),
            (DeclarationStatus.Submitted, "Depus prin SPV"),
            (DeclarationStatus.Accepted, "Recipisa nr. INTERNT-123"),
        ]);
        Guid? receipt = (await _db.DeclarationVersions.SingleAsync(v => v.Id == d301)).ReceiptDocumentId;
        (await _db.Documents.SingleAsync(d => d.Id == receipt)).Origin.ShouldBe(Domain.Documents.DocumentOrigin.AccountingUpload);
        (await _db.AuditLogs.Where(a => a.EntityId == d301.ToString()).Select(a => a.Action).ToListAsync())
            .ShouldBe(["GENERATE", "VALIDATE", "MARK_SIGNED", "MARK_SUBMITTED", "RECEIPT"], ignoreOrder: true);
    }

    [Fact]
    public async Task Invalid_transitions_are_refused()
    {
        await GenerateIon();
        Guid d100 = await IonVersion(DeclarationType.D100);

        (await Transition(d100, DeclarationAction.MarkSigned)).Error.Description.ShouldBe("Acțiunea MARK_SIGNED nu e permisă din statusul GENERATED.");
        (await Transition(d100, DeclarationAction.Regenerate)).Error.Code.ShouldBe("Accounting.InvalidTransition");
        (await Receipt(d100, null)).Error.Description.ShouldBe("Recipisa se încarcă doar pe o declarație depusă.");
        Guid declarationId = (await _db.DeclarationVersions.SingleAsync(v => v.Id == d100)).DeclarationId;
        (await Rectify(declarationId, "Corecție")).Error.Description.ShouldBe("Rectificativa se creează doar dintr-o versiune cu recipisă validă.");

        await Transition(d100, DeclarationAction.Validate);
        await Transition(d100, DeclarationAction.MarkSigned);
        await Transition(d100, DeclarationAction.MarkSubmitted);
        Result<DeclarationVersionDto> noReason = await Transition(d100, DeclarationAction.MarkRejected, "  ");
        (noReason.Error.Code, noReason.Error.Type).ShouldBe(("Accounting.ReasonRequired", ErrorType.Problem));
        (await Rectify(declarationId, " ")).Error.Code.ShouldBe("Accounting.ReasonRequired");

        Result<DeclarationVersionDto> text = await new UploadDeclarationReceiptCommandHandler(_db, User(), Actions())
            .Handle(new UploadDeclarationReceiptCommand(d100, new ReceiptFile("recipisa.txt", "text/plain", [1]), null), CancellationToken.None);
        text.Error.Code.ShouldBe("Accounting.ReceiptFileType");
    }

    [Fact]
    public async Task Rejected_declaration_is_regenerated_on_the_same_version()
    {
        await GenerateIon();
        Guid d301 = await IonVersion(DeclarationType.D301);
        await Transition(d301, DeclarationAction.Validate);
        await Transition(d301, DeclarationAction.MarkSigned);
        await Transition(d301, DeclarationAction.MarkSubmitted);
        (await Transition(d301, DeclarationAction.MarkRejected, "Respinsă: cont eronat")).Value.Status.ShouldBe(DeclarationStatus.Rejected);
        Guid? oldXml = (await _db.DeclarationVersions.SingleAsync(v => v.Id == d301)).XmlDocumentId;
        await ChangeCommission(Platform.Bolt, 1100m);

        DeclarationVersionDto regenerated = (await Transition(d301, DeclarationAction.Regenerate, "Factura Bolt corectată")).Value;

        (regenerated.Status, regenerated.VersionNo, regenerated.Amount, regenerated.HasPdf, regenerated.HasXml).ShouldBe((DeclarationStatus.Generated, 1, 357m, false, true));
        DeclarationVersion version = await _db.DeclarationVersions.SingleAsync(v => v.Id == d301);
        version.XmlDocumentId.ShouldNotBe(oldXml);
        version.ValidationResultJson.ShouldBeNull();
        List<DeclarationLine> lines = await _db.DeclarationLines.Where(l => l.DeclarationVersionId == d301).ToListAsync();
        lines.Count(l => l.SupersededAtUtc != null).ShouldBe(2);
        lines.Where(l => l.SupersededAtUtc == null).Sum(l => l.Value).ShouldBe(357m);
        (await new GetDeclarationBreakdownQueryHandler(_db).Handle(new GetDeclarationBreakdownQuery(d301), CancellationToken.None)).Value.Lines.Count.ShouldBe(2);

        (await Transition(d301, DeclarationAction.Validate)).Value.Status.ShouldBe(DeclarationStatus.ReadyToSign);
        AuditLog audit = await _db.AuditLogs.SingleAsync(a => a.EntityId == d301.ToString() && a.Action == "REGENERATE");
        (audit.Reason, audit.BeforeJson, audit.AfterJson).ShouldBe(("Factura Bolt corectată", "{\"status\":\"REJECTED\",\"amount\":336}", "{\"status\":\"GENERATED\",\"amount\":357}"));
    }

    [Fact]
    public async Task Rectification_is_a_new_version_that_unlocks_its_documents()
    {
        await GenerateIon();
        Guid d301 = await IonVersion(DeclarationType.D301);
        await Accept(d301);
        PlatformDocument boltInvoice = await _db.PlatformDocuments.SingleAsync(d => d.PfaRegistrationId == _ion && d.Platform == Platform.Bolt && d.DocumentType == PlatformDocumentType.CommissionInvoice);
        (await PlatformDocumentSupport.LockReasonAsync(_db, boltInvoice, CancellationToken.None)).ShouldNotBeNull();
        Guid declarationId = (await _db.DeclarationVersions.SingleAsync(v => v.Id == d301)).DeclarationId;

        DeclarationVersionDto rectification = (await Rectify(declarationId, "Comision Bolt greșit")).Value;

        (rectification.VersionNo, rectification.Kind, rectification.Status, rectification.RectificationReason, rectification.HasXml)
            .ShouldBe((2, DeclarationVersionKind.Rectificative, DeclarationStatus.Generated, "Comision Bolt greșit", true));
        (await _db.DeclarationVersions.SingleAsync(v => v.Id == d301)).Status.ShouldBe(DeclarationStatus.Accepted);
        (await PlatformDocumentSupport.LockReasonAsync(_db, boltInvoice, CancellationToken.None)).ShouldBeNull();
        (await PlatformDocumentSupport.IncludedInAsync(_db, boltInvoice.Id, CancellationToken.None))
            .Where(r => r.Type == DeclarationType.D301).Select(r => (r.VersionNo, r.Status))
            .ShouldBe([(1, DeclarationStatus.Accepted), (2, DeclarationStatus.Generated)], ignoreOrder: true);
        DeclarationFile xml = (await new GetDeclarationFileQueryHandler(_db, Files()).Handle(new GetDeclarationFileQuery(rectification.Id, DeclarationFileKind.Xml), CancellationToken.None)).Value;
        xml.FileName.ShouldBe("D301_12345674_2026-08_v2.xml");
        System.Text.Encoding.UTF8.GetString(xml.Content).ShouldContain("d_rec=\"1\"");
        (await Transition(d301, DeclarationAction.Validate)).Error.Code.ShouldBe("Accounting.NotCurrentVersion");

        // Documentul se corectează; rectificativa, generată înainte, nu mai corespunde.
        await ChangeCommission(Platform.Bolt, 1100m);
        (await Transition(rectification.Id, DeclarationAction.Validate)).Value.Status.ShouldBe(DeclarationStatus.ValidationFailed);
        ValidationResult failed = (await new GetDeclarationValidationQueryHandler(_db).Handle(new GetDeclarationValidationQuery(rectification.Id), CancellationToken.None)).Value!;
        failed.Levels[0].Messages.Select(m => m.Text).ShouldContain("Documentele lunii s-au schimbat după generare: declarația trebuie regenerată („Regenerează”).");

        (await Transition(rectification.Id, DeclarationAction.Regenerate)).Value.Amount.ShouldBe(357m);
        (await Transition(rectification.Id, DeclarationAction.Validate)).Value.Status.ShouldBe(DeclarationStatus.ReadyToSign);
    }

    [Fact]
    public async Task D100_rectification_waits_for_the_d710_procedure()
    {
        await GenerateIon();
        Guid d100 = await IonVersion(DeclarationType.D100);
        await Accept(d100);
        Guid declarationId = (await _db.DeclarationVersions.SingleAsync(v => v.Id == d100)).DeclarationId;

        DeclarationVersionDto rectification = (await Rectify(declarationId, "Cotă corectată")).Value;
        rectification.HasXml.ShouldBeFalse();
        int calls = _anaf.Calls.Count;

        (await Transition(rectification.Id, DeclarationAction.Validate)).Value.Status.ShouldBe(DeclarationStatus.ValidationFailed);

        _anaf.Calls.Count.ShouldBe(calls);
        ValidationResult result = (await new GetDeclarationValidationQueryHandler(_db).Handle(new GetDeclarationValidationQuery(rectification.Id), CancellationToken.None)).Value!;
        result.Levels.Single(l => l.Level == ValidationLevel.Xsd).Messages[0].Text.ShouldStartWith("Corecția D100 se depune prin D710");
    }

    [Fact]
    public async Task Regeneration_needs_a_ready_month()
    {
        await GenerateIon();
        Guid d301 = await IonVersion(DeclarationType.D301);
        await Transition(d301, DeclarationAction.Validate);
        await Transition(d301, DeclarationAction.MarkSigned);
        await Transition(d301, DeclarationAction.MarkSubmitted);
        await Transition(d301, DeclarationAction.MarkRejected, "Respinsă");
        PlatformDocument invoice = await _db.PlatformDocuments.SingleAsync(d => d.PfaRegistrationId == _ion && d.Platform == Platform.Uber && d.DocumentType == PlatformDocumentType.CommissionInvoice);
        invoice.Status = PlatformDocumentStatus.NeedsReview;
        await _db.SaveChangesAsync();

        Result<DeclarationVersionDto> regenerated = await Transition(d301, DeclarationAction.Regenerate);

        regenerated.Error.Code.ShouldBe("Accounting.MonthNotReady");
        (await _db.DeclarationVersions.SingleAsync(v => v.Id == d301)).Status.ShouldBe(DeclarationStatus.Rejected);
    }

    // ─── Ajutoare ──────────────────────────────────────────────────────────────────────────────

    private async Task<Guid> IonVersion(DeclarationType type) => (await IonVersions()).Single(v => v.Declaration.Type == type).Id;

    private Task<Result<DeclarationVersionDto>> Transition(Guid versionId, DeclarationAction action, string? note = null) =>
        new TransitionDeclarationVersionCommandHandler(_db, User(), Actions()).Handle(new TransitionDeclarationVersionCommand(versionId, action, note), CancellationToken.None);

    private Task<Result<DeclarationVersionDto>> Receipt(Guid versionId, string? number) =>
        new UploadDeclarationReceiptCommandHandler(_db, User(), Actions())
            .Handle(new UploadDeclarationReceiptCommand(versionId, new ReceiptFile("recipisa.pdf", "application/pdf", "%PDF recipisa"u8.ToArray()), number), CancellationToken.None);

    private Task<Result<DeclarationVersionDto>> Rectify(Guid declarationId, string? reason) =>
        new CreateRectificationCommandHandler(_db, User(), Actions()).Handle(new CreateRectificationCommand(declarationId, reason), CancellationToken.None);

    private async Task Accept(Guid versionId)
    {
        await Transition(versionId, DeclarationAction.Validate);
        await Transition(versionId, DeclarationAction.MarkSigned);
        await Transition(versionId, DeclarationAction.MarkSubmitted);
        await Receipt(versionId, "R-1");
    }

    /// <summary>Corectura unei facturi: comisionul citit se schimbă (textul PDF îl conține).</summary>
    private async Task ChangeCommission(Platform platform, decimal commission)
    {
        PlatformDocument invoice = await _db.PlatformDocuments.SingleAsync(d => d.PfaRegistrationId == _ion && d.Platform == platform && d.DocumentType == PlatformDocumentType.CommissionInvoice);
        DocumentExtraction extraction = await _db.DocumentExtractions.SingleAsync(e => e.PlatformDocumentId == invoice.Id && e.IsCurrent);
        extraction.CommissionAmount = commission;
        extraction.Amount = commission;
        await _db.SaveChangesAsync();
    }

    private async Task GenerateIon()
    {
        await RunJob(BackgroundJobType.ProcessPeriod);
        await new ConfirmCleanDocumentsCommandHandler(_db, User(), _options).Handle(new ConfirmCleanDocumentsCommand(Period), CancellationToken.None);
        await RunJob(BackgroundJobType.GenerateDeclarations);
    }

    private Task<List<DeclarationVersion>> IonVersions() =>
        _db.DeclarationVersions.Include(v => v.Declaration).Where(v => v.Declaration.PfaRegistrationId == _ion).ToListAsync();

    private async Task<JobDto> RunJob(BackgroundJobType type)
    {
        JobRef started = (await new StartMonthJobCommandHandler(_db, User()).Handle(new StartMonthJobCommand(type, Period), CancellationToken.None)).Value;
        Result run = await new RunMonthJobCommandHandler(_db, new NoExtraction(), Files(), Validator(), _options).Handle(new RunMonthJobCommand(started.JobId), CancellationToken.None);
        run.IsSuccess.ShouldBeTrue();
        return (await new GetJobQueryHandler(_db).Handle(new GetJobQuery(started.JobId), CancellationToken.None)).Value;
    }

    private DeclarationFiles Files() => new(_db, new AnafDeclarationXmlService(), _files, new PlainSecrets());

    private DeclarationValidator Validator() => new(_db, new AnafDeclarationXmlService(), _anaf, Files(), _options);

    private DeclarationActions Actions() => new(_db, Files(), Validator(), _options);

    private async Task<PeriodOverview> Overview() =>
        (await new GetPeriodOverviewQueryHandler(_db, _options).Handle(new GetPeriodOverviewQuery(Period), CancellationToken.None)).Value;

    private async Task<IReadOnlyList<DeclarationSummary>> Declarations(Guid pfaId) =>
        (await new ListDeclarationsQueryHandler(_db, _options).Handle(new ListDeclarationsQuery(pfaId, Period), CancellationToken.None)).Value;

    private static OverviewRow Row(PeriodOverview overview, Guid pfaId) => overview.Rows.Single(r => r.PfaId == pfaId);

    private Guid Pfa(string name, bool onboarded = true)
    {
        string[] names = name.Split(' ');
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@ridelance.ro", FirstName = names[0], LastName = names[^1], Role = UserRole.Client };
        var pfa = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            FullName = name,
            LegalName = $"{names[^1].ToUpperInvariant()} {names[0].ToUpperInvariant()} PFA",
            // CUI fictiv, cu cifra de control corectă.
            Cui = "12345674",
            Street = "Str. Exemplu",
            Number = "1",
            City = "București",
            OnboardingCompletedAtUtc = onboarded ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
        };
        _db.PfaRegistrations.Add(pfa);
        _db.PfaBankAccountDeclarations.Add(new PfaBankAccountDeclaration
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa.Id,
            BankName = "Banca Transilvania",
            IbanEncrypted = "RO49AAAA1B31007593840000",
        });
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
        _db.AnafDeclarationSchemas.AddRange(
            Schema(DeclarationType.D100, "v2-20220224", AnafSchemaCorrections.D100V2),
            Schema(DeclarationType.D301, "v1-20200130", AnafSchemaCorrections.D301V1),
            Schema(DeclarationType.D390, "v3-20210212", AnafSchemaCorrections.D390V3));
    }

    private static AnafDeclarationSchema Schema(DeclarationType type, string version, string xsd) => new()
    {
        Id = Guid.NewGuid(),
        DeclarationType = type,
        Version = version,
        XsdPath = xsd,
        ValidatorVersion = "2026-09",
        ValidFrom = new DateOnly(2025, 1, 1),
    };

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
