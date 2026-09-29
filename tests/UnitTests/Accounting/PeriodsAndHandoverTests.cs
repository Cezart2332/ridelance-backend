using System.IO.Compression;
using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.Handover;
using Application.Accounting.Ledger;
using Application.Accounting.Periods;
using Application.Accounting.Pfas;
using Application.Accounting.Registers;
using Domain.Accounting;
using Domain.Banking;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Accounting;
using Infrastructure.Accounting.Anaf;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>B8: perioadele contabile, corecțiile controlate, inactivarea, retenția și dosarul de predare.</summary>
public sealed class PeriodsAndHandoverTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Guid _accountant = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _pfa = Guid.NewGuid();
    private readonly MemoryFiles _files = new();

    public PeriodsAndHandoverTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        _db.Users.Add(new User { Id = _accountant, Email = "contabil@ridelance.ro", FirstName = "Contabil", LastName = "RIDElance", Role = UserRole.Contabil });
        var user = new User { Id = _user, Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu" };
        _db.PfaRegistrations.Add(new PfaRegistration { Id = _pfa, UserId = _user, User = user, FullName = "Ion Popescu", LegalName = "POPESCU ION PFA", Cui = "12345674" });
        _db.PfaAccountingEngagements.Add(new PfaAccountingEngagement { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, StartDate = new DateOnly(2026, 7, 15), Status = EngagementStatus.Active });
        _db.RetentionPolicies.Add(new RetentionPolicy { Id = Guid.NewGuid(), YearsAfter = 5, StartMonth = 7, StartDay = 1, ValidFrom = new DateOnly(2000, 1, 1) });
        _db.ExpenseCategoryRules.Add(new ExpenseCategoryRule { Id = Guid.NewGuid(), Category = "FUEL", Label = "Combustibil", DefaultDeductibility = DeductibilityType.Percent100, ValidFrom = new DateOnly(2025, 1, 1) });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Retention_is_july_first_of_the_next_year_plus_five_years_minus_a_day() =>
        RetentionService.MinimumRetentionUntil(2026, new RetentionPolicy { YearsAfter = 5, StartMonth = 7, StartDay = 1 }).ShouldBe(new DateOnly(2032, 6, 30));

    [Fact]
    public async Task Periods_run_from_the_engagement_start_and_show_who_closed_them()
    {
        (await Close("2026-08")).IsSuccess.ShouldBeTrue();

        IReadOnlyList<AccountingPeriodDto> periods = (await new ListPeriodsQueryHandler(_db).Handle(new ListPeriodsQuery(_pfa), CancellationToken.None)).Value;

        periods.Select(p => p.Period).ShouldContain("2026-07");
        periods.Select(p => p.Period).ShouldBe(periods.Select(p => p.Period).OrderByDescending(p => p, StringComparer.Ordinal));
        AccountingPeriodDto august = periods.Single(p => p.Period == "2026-08");
        (august.Status, august.ClosedBy!.Name).ShouldBe((AccountingPeriodStatus.Closed, "Contabil RIDElance"));
        periods.Single(p => p.Period == "2026-07").Status.ShouldBe(AccountingPeriodStatus.Open);
    }

    [Fact]
    public async Task Closing_a_month_locks_its_entries_and_blocks_writes()
    {
        LedgerEntry entry = Entry(new DateOnly(2026, 8, 10), -300m);
        await _db.SaveChangesAsync();

        AccountingPeriodDto closed = (await Close("2026-08")).Value;

        closed.Status.ShouldBe(AccountingPeriodStatus.Closed);
        (await _db.LedgerEntries.SingleAsync()).Status.ShouldBe(LedgerEntryStatus.Locked);
        (await Close("2026-08")).Error.Description.ShouldBe("Perioada 2026-08 e deja închisă.");
        (await Close("2030-01")).Error.Code.ShouldBe("Accounting.PeriodNotEnded");
        (await Close("2025-01")).Error.Code.ShouldBe("Accounting.PeriodOutsideEngagement");
        (await new UpdateLedgerEntryCommandHandler(_db, User())
            .Handle(new UpdateLedgerEntryCommand(entry.Id, Json("""{"amount":-250}"""), "Corectură"), CancellationToken.None)).Error.Code.ShouldBe("Accounting.LedgerEntryLocked");
        (await new CreateManualLedgerEntryCommandHandler(_db, User()).Handle(new CreateManualLedgerEntryCommand(_pfa, new ManualLedgerEntryRequest(
            new DateOnly(2026, 8, 20), "", null, "Notă", LedgerTransactionType.Expense, PaymentMethod.Cash, 10m, null, "Motiv")), CancellationToken.None))
            .Error.Code.ShouldBe("Accounting.PeriodClosed");
        (await _db.AuditLogs.SingleAsync(a => a.Action == "CLOSE")).EntityId.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_correction_changes_a_locked_entry_only_through_the_closed_month()
    {
        LedgerEntry entry = Entry(new DateOnly(2026, 8, 10), -300m);
        await _db.SaveChangesAsync();
        (await Correct("2026-08", entry.Id, """{"amount":-280}""", "Bon greșit")).Error.Code.ShouldBe("Accounting.PeriodOpen");
        await Close("2026-08");

        (await Correct("2026-08", entry.Id, """{"amount":-280}""", " ")).Error.Code.ShouldBe("Accounting.ReasonRequired");
        (await Correct("2026-08", entry.Id, """{"date":"2026-09-01"}""", "Mutare")).Error.Code.ShouldBe("Accounting.EntryOutsidePeriod");
        PeriodCorrectionDto correction = (await Correct("2026-08", entry.Id, """{"amount":-280}""", "Bon greșit")).Value;

        (correction.Period, correction.LedgerEntryId, correction.Reason, correction.By.Name).ShouldBe(("2026-08", (Guid?)entry.Id, "Bon greșit", "Contabil RIDElance"));
        correction.Change.GetProperty("amount").GetDecimal().ShouldBe(-280m);
        LedgerEntry corrected = await _db.LedgerEntries.SingleAsync();
        (corrected.Amount, corrected.Status).ShouldBe((-280m, LedgerEntryStatus.Locked));
        (await _db.AuditLogs.SingleAsync(a => a.Action == "PERIOD_CORRECTION")).Reason.ShouldBe("Bon greșit");
        (await _db.PeriodCorrections.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_correction_brings_a_closed_month_import_into_the_registers()
    {
        await Close("2026-08");
        LedgerEntry late = Entry(new DateOnly(2026, 8, 30), -120m);
        late.Status = LedgerEntryStatus.NeedsReview;
        late.ClosedPeriodFlag = true;
        await _db.SaveChangesAsync();
        (await Rjip()).Rows.ShouldBeEmpty();

        await Correct("2026-08", late.Id, """{"category":"FUEL"}""", "Plată importată după închidere");

        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        (entry.ClosedPeriodFlag, entry.Status, entry.Category, entry.DeductibleAmount).ShouldBe((false, LedgerEntryStatus.Locked, "FUEL", (decimal?)120m));
        (await Rjip()).Rows.ShouldHaveSingleItem().BankOut.ShouldBe(120m);
    }

    [Fact]
    public async Task Deactivation_makes_the_dossier_read_only_with_its_retention()
    {
        DeactivatePfaCommandHandler handler = new(_db, User());

        (await handler.Handle(new DeactivatePfaCommand(_pfa, new DateOnly(2026, 7, 1)), CancellationToken.None)).Error.Code.ShouldBe("Accounting.InvalidEndDate");
        PfaAccountingSummary summary = (await handler.Handle(new DeactivatePfaCommand(_pfa, new DateOnly(2026, 8, 31)), CancellationToken.None)).Value;

        (summary.ReadOnly, summary.Engagement.Status, summary.Engagement.EndDate, summary.RetentionUntil)
            .ShouldBe((true, EngagementStatus.Inactive, (DateOnly?)new DateOnly(2026, 8, 31), (DateOnly?)new DateOnly(2032, 6, 30)));
        (await handler.Handle(new DeactivatePfaCommand(_pfa, new DateOnly(2026, 9, 30)), CancellationToken.None)).Error.Code.ShouldBe("Accounting.PfaReadOnly");
        (await Close("2026-08")).Error.Code.ShouldBe("Accounting.PfaReadOnly");
        (await _db.AuditLogs.SingleAsync(a => a.Action == "DEACTIVATE")).AfterJson!.ShouldContain("2026-08-31");
    }

    [Fact]
    public async Task Importers_ignore_operations_after_the_end_date()
    {
        var connection = new BankConnection { Id = Guid.NewGuid(), UserId = _user, Provider = "test", InstitutionId = "BT", InstitutionName = "BT", ProviderConsentId = "consent-period", Status = BankConnectionStatus.Linked };
        var account = new BankAccount { Id = Guid.NewGuid(), BankConnectionId = connection.Id, UserId = _user, ProviderAccountId = "a", IsActive = true };
        _db.BankConnections.Add(connection);
        _db.BankAccounts.Add(account);
        foreach ((DateOnly date, decimal amount) in new[] { (new DateOnly(2026, 8, 20), -50m), (new DateOnly(2026, 9, 5), -70m) })
        {
            _db.BankTransactions.Add(new BankTransaction
            {
                Id = Guid.NewGuid(), BankAccountId = account.Id, UserId = _user, ProviderTransactionId = Guid.NewGuid().ToString("N"),
                ProviderConsentId = "consent-period",
                BookingDate = date, Amount = amount, Currency = "RON", CounterpartyName = "MAGAZIN", ImportedAtUtc = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync();
        await new DeactivatePfaCommandHandler(_db, User()).Handle(new DeactivatePfaCommand(_pfa, new DateOnly(2026, 8, 31)), CancellationToken.None);

        await new RunLedgerImportCommandHandler(_db, [new BankLedgerSource(_db)], Options.Create(new AccountingOptions()))
            .Handle(new RunLedgerImportCommand(_pfa), CancellationToken.None);

        (await _db.LedgerEntries.Select(e => e.Amount).ToListAsync()).ShouldBe([-50m]);
    }

    [Fact]
    public async Task Summary_shows_the_dossier_header()
    {
        _db.PfaAccountingSettings.AddRange(
            Setting(PfaAccountingSettingKeys.Art317, "true", new DateOnly(2026, 7, 15)),
            Setting(PfaAccountingSettingKeys.Platforms, "[\"BOLT\"]", new DateOnly(2026, 7, 15)));
        await _db.SaveChangesAsync();

        PfaAccountingSummary summary = (await new GetPfaSummaryQueryHandler(_db).Handle(new GetPfaSummaryQuery(_pfa), CancellationToken.None)).Value;

        (summary.Name, summary.Cui, summary.Art317, summary.Art317ActivationDate, summary.ReadOnly, summary.RetentionUntil)
            .ShouldBe(("POPESCU ION PFA", "12345674", true, (DateOnly?)new DateOnly(2026, 7, 15), false, (DateOnly?)null));
        summary.Platforms.ShouldBe([Platform.Bolt]);
        summary.Cash.Status.ShouldBe(CashRegisterStatus.NotRequiredCurrentConfiguration);
        summary.CurrentPeriod.ShouldBe(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Handover_package_has_the_registers_ledger_documents_declarations_and_summary()
    {
        Entry(new DateOnly(2026, 8, 10), -300m);
        await new DeactivatePfaCommandHandler(_db, User()).Handle(new DeactivatePfaCommand(_pfa, new DateOnly(2026, 8, 31)), CancellationToken.None);
        DeclarationFiles files = Files();
        Document invoice = await files.StoreAsync(_pfa, "%PDF factura"u8.ToArray(), "factura-bolt.pdf", "application/pdf", CancellationToken.None, DocumentOrigin.AccountingUpload);
        _db.PlatformDocuments.Add(new PlatformDocument
        {
            Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Period = "2026-08", Platform = Platform.Bolt, DocumentType = PlatformDocumentType.CommissionInvoice,
            SourceDocumentId = invoice.Id, FileHash = "h", Status = PlatformDocumentStatus.Confirmed,
        });
        Document xml = await files.StoreAsync(_pfa, "<xml/>"u8.ToArray(), "D301_12345674_2026-08_v1.xml", "application/xml", CancellationToken.None);
        var declaration = new Declaration { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Period = "2026-08", Type = DeclarationType.D301 };
        _db.Declarations.Add(declaration);
        _db.DeclarationVersions.Add(new DeclarationVersion { Id = Guid.NewGuid(), DeclarationId = declaration.Id, VersionNo = 1, XmlDocumentId = xml.Id });
        await _db.SaveChangesAsync();

        JobRef job = (await new StartHandoverPackageCommandHandler(_db, User()).Handle(new StartHandoverPackageCommand(_pfa), CancellationToken.None)).Value;
        (await Handover().Handle(new RunHandoverPackageCommand(job.JobId), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        BackgroundJob done = await _db.BackgroundJobs.SingleAsync();
        (done.Status, done.ProgressDone, done.ProgressTotal).ShouldBe((BackgroundJobStatus.Completed, 7, 7));
        RegisterFile file = (await new GetJobFileQueryHandler(_db, files).Handle(new GetJobFileQuery(job.JobId), CancellationToken.None)).Value;
        (file.FileName, file.ContentType).ShouldBe(("RIDElance_PFA_12345674_2026.zip", "application/zip"));

        using var zip = new ZipArchive(new MemoryStream(file.Content));
        List<string> entries = [.. zip.Entries.Select(e => e.FullName)];
        entries.ShouldContain("01_Registre/RJIP_12345674_20260715_20260831.pdf");
        entries.ShouldContain("01_Registre/RJIP_12345674_20260715_20260831.xlsx");
        // Anul nu e închis: REF-ul e situația intermediară la încheierea colaborării.
        entries.ShouldContain("01_Registre/REF_12345674_2026_20260831.pdf");
        entries.ShouldContain("01_Registre/Registru-inventar_12345674_20260831.xlsx");
        entries.ShouldContain("02_Ledger/Ledger_12345674_20260715_20260831.xlsx");
        entries.ShouldContain("03_Documente/Platforme/2026-08/Bolt/factura-bolt.pdf");
        entries.ShouldContain("04_Declaratii/2026-08/D301_v1/D301_12345674_2026-08_v1.xml");
        entries.ShouldContain("Sumar_predare.pdf");
        JsonDocument.Parse(done.ResultJson).RootElement.GetProperty("results").GetArrayLength().ShouldBe(7);
    }

    // ─── Ajutoare ──────────────────────────────────────────────────────────────────────────────

    private RunHandoverPackageCommandHandler Handover()
    {
        var exporter = new RegisterExporter();
        var refQuery = new GetRefQueryHandler(_db);
        return new RunHandoverPackageCommandHandler(
            _db,
            Files(),
            exporter,
            refQuery,
            new ExportRjipQueryHandler(_db, new GetRjipQueryHandler(_db), exporter),
            new ExportRefQueryHandler(_db, refQuery, exporter),
            new ExportInventoryQueryHandler(_db, exporter));
    }

    private DeclarationFiles Files() => new(_db, new AnafDeclarationXmlService(), _files, new PlainSecrets());

    private Task<Result<AccountingPeriodDto>> Close(string period) =>
        new ClosePeriodCommandHandler(_db, User()).Handle(new ClosePeriodCommand(_pfa, period), CancellationToken.None);

    private Task<Result<PeriodCorrectionDto>> Correct(string period, Guid? entryId, string change, string reason) =>
        new CreatePeriodCorrectionCommandHandler(_db, User()).Handle(new CreatePeriodCorrectionCommand(_pfa, period, entryId, Json(change), reason), CancellationToken.None);

    private async Task<RjipView> Rjip() =>
        (await new GetRjipQueryHandler(_db).Handle(new GetRjipQuery(_pfa, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), CancellationToken.None)).Value;

    private LedgerEntry Entry(DateOnly date, decimal amount)
    {
        var entry = new LedgerEntry
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = _pfa,
            Date = date,
            DocumentLabel = "Extras",
            Source = LedgerSource.Bank,
            BankTransactionId = Guid.NewGuid(),
            Description = "Plată",
            TransactionType = LedgerTransactionType.Expense,
            PaymentMethod = PaymentMethod.Bank,
            Amount = amount,
            Status = LedgerEntryStatus.AutoImported,
            AccountingPeriod = LedgerSupport.PeriodOf(date),
        };
        _db.LedgerEntries.Add(entry);
        return entry;
    }

    private PfaAccountingSetting Setting(string key, string value, DateOnly from) => new()
    {
        Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Key = key, ValueJson = value, ValidFrom = from, Note = "Setare", ChangedByUserId = _accountant,
    };

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private FixedUser User() => new(_accountant);

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
