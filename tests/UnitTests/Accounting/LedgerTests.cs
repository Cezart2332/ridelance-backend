using System.Text.Json;
using Application.Abstractions.Ai;
using Application.Abstractions.Authentication;
using Application.Abstractions.Services;
using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.Ledger;
using Application.Invoicing;
using Domain.Accounting;
using Domain.Banking;
using Domain.Documents;
using Domain.Invoicing;
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

/// <summary>B6: importul și clasificarea ledger-ului, deductibilitatea, documentele și rapoartele Z.</summary>
public sealed class LedgerTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 10);

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Guid _accountant = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _pfa = Guid.NewGuid();
    private readonly Guid _account = Guid.NewGuid();
    private readonly FakeReceipts _receipts = new();
    private readonly FakeOblio _oblio = new();
    private readonly MemoryFiles _files = new();
    private AccountingOptions _options = new();

    public LedgerTests()
    {
        _db.Users.Add(new User { Id = _accountant, Email = "contabil@ridelance.ro", FirstName = "Contabil", LastName = "RIDElance", Role = UserRole.Contabil });
        var user = new User { Id = _user, Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu" };
        _db.PfaRegistrations.Add(new PfaRegistration { Id = _pfa, UserId = _user, User = user, FullName = "Ion Popescu", Cui = "12345674" });
        var connection = new BankConnection { Id = Guid.NewGuid(), UserId = _user, Provider = "test", InstitutionId = "BT", InstitutionName = "Banca Transilvania" };
        _db.BankConnections.Add(connection);
        _db.BankAccounts.Add(new BankAccount { Id = _account, BankConnectionId = connection.Id, UserId = _user, ProviderAccountId = "acc", IsActive = true });
        _db.ExpenseCategoryRules.AddRange(
            Category("FUEL", vehicle: true, DeductibilityType.Percent100, "OMV|PETROM"),
            Category("CAR_SERVICE", vehicle: true, DeductibilityType.Percent100, "SERVICE"),
            Category("PHONE", vehicle: false, DeductibilityType.Percent100, "ORANGE"),
            Category("DEPRECIATION", vehicle: true, DeductibilityType.SpecialRule, null),
            Category("PERSONAL", vehicle: false, DeductibilityType.NonDeductible, null));
        VehicleDeductibility(new DateOnly(2026, 1, 1), "100_PERCENT");
        _db.CashRegisterStates.Add(new CashRegisterState { PfaRegistrationId = _pfa, CashRequested = true, CashEnabled = true, Status = CashRegisterStatus.Active, ActivationDate = new DateOnly(2026, 9, 1) });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Spec §5.3: o zi în RIDElance (10.10.2026, deductibilitate 100%, cash activ).</summary>
    [Fact]
    public async Task A_day_in_ridelance()
    {
        Transaction(-300m, "OMV PETROM SA", "Plata card OMV Petrom Brasov");
        Transaction(1850m, "BOLT OPERATIONS OU", "Payout saptamana 40");
        _receipts.Z = new ZReportReading(Day, "125", 420m);

        IReadOnlyList<LedgerImportResult> imported = await Import();
        ZReportUploadResult z = (await UploadZ()).Value;

        imported.Single(r => r.Source == LedgerSource.Bank).Created.ShouldBe(2);
        List<LedgerEntry> entries = await _db.LedgerEntries.OrderBy(e => e.Amount).ToListAsync();
        LedgerEntry fuel = entries[0];
        (fuel.TransactionType, fuel.Category, fuel.VehicleRelated, fuel.Amount, fuel.DeductibleAmount, fuel.DeductibilityType, fuel.Status)
            .ShouldBe((LedgerTransactionType.Expense, "FUEL", true, -300m, (decimal?)300m, (DeductibilityType?)DeductibilityType.Percent100, LedgerEntryStatus.AutoImported));
        (fuel.DocumentLabel, fuel.PaymentMethod, fuel.AccountingPeriod).ShouldBe(("Extras 10.10.2026", PaymentMethod.Bank, "2026-10"));

        LedgerEntry bolt = entries.Single(e => e.Amount == 1850m);
        (bolt.TransactionType, bolt.Source, bolt.PaymentMethod, bolt.Status).ShouldBe((LedgerTransactionType.Income, LedgerSource.Bolt, PaymentMethod.Bank, LedgerEntryStatus.AutoImported));

        (z.Extracted, z.LedgerEntry.Amount, z.LedgerEntry.Source, z.LedgerEntry.PaymentMethod, z.LedgerEntry.TransactionType, z.LedgerEntry.DocumentLabel, z.LedgerEntry.Status)
            .ShouldBe((new ZReportExtracted(Day, "125", 420m), 420m, LedgerSource.CashZ, PaymentMethod.Cash, LedgerTransactionType.Income, "Raport Z nr. 125", LedgerEntryStatus.NeedsReview));
        (await _db.ZReports.SingleAsync()).LedgerEntryId.ShouldBe(z.LedgerEntry.Id);
    }

    [Fact]
    public async Task Import_is_idempotent_and_unknown_transactions_need_review()
    {
        Transaction(-300m, "OMV PETROM SA", null);
        Transaction(-49.99m, "MAGAZIN X", "Cumparaturi");
        Transaction(200m, "POPESCU ION", "Depunere");

        await Import();
        IReadOnlyList<LedgerImportResult> again = await Import();

        again.Sum(r => r.Created + r.Updated).ShouldBe(0);
        (await _db.LedgerEntries.CountAsync()).ShouldBe(3);
        LedgerEntry unknownExpense = await _db.LedgerEntries.SingleAsync(e => e.Amount == -49.99m);
        (unknownExpense.Category, unknownExpense.Status).ShouldBe((null, LedgerEntryStatus.NeedsReview));
        LedgerEntry unknownIncome = await _db.LedgerEntries.SingleAsync(e => e.Amount == 200m);
        (unknownIncome.TransactionType, unknownIncome.Status).ShouldBe((LedgerTransactionType.Other, LedgerEntryStatus.NeedsReview));
    }

    /// <summary>Spec §5.2: deductibilitatea urmează setarea valabilă la data cheltuielii.</summary>
    [Fact]
    public async Task Deductibility_follows_the_setting_valid_at_the_expense_date()
    {
        VehicleDeductibility(new DateOnly(2027, 1, 1), "50_PERCENT");
        VehicleDeductibility(new DateOnly(2027, 7, 1), "100_PERCENT");
        await _db.SaveChangesAsync();
        Transaction(-1000m, "AUTO SERVICE SRL", null, new DateOnly(2027, 3, 15));
        Transaction(-1000m, "AUTO SERVICE SRL", null, new DateOnly(2027, 8, 15));

        await Import();

        List<LedgerEntry> services = await _db.LedgerEntries.OrderBy(e => e.Date).ToListAsync();
        services.Select(e => (e.Category, e.DeductiblePercent, e.DeductibleAmount, e.DeductibilityValidFrom))
            .ShouldBe([("CAR_SERVICE", (decimal?)50m, (decimal?)500m, (DateOnly?)new DateOnly(2027, 1, 1)), ("CAR_SERVICE", 100m, 1000m, new DateOnly(2027, 7, 1))]);
        LedgerEntryDto dto = (await List()).Items.First(e => e.Date == new DateOnly(2027, 3, 15));
        dto.DeductibilityRule.ShouldBe(new DeductibilityRuleRef(PfaAccountingSettingKeys.VehicleDeductibility, services[0].DeductibilityRuleId, new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Depreciation_is_a_special_rule_not_the_vehicle_percent()
    {
        var entry = new LedgerEntry { Date = Day, TransactionType = LedgerTransactionType.Expense, Category = "DEPRECIATION", Amount = -500m };

        DeductibilityService.Resolve(entry, new LedgerRules([.. _db.ExpenseCategoryRules], [.. _db.PfaAccountingSettings]));

        (entry.DeductibilityType, entry.DeductiblePercent, entry.DeductibleAmount, entry.VehicleRelated).ShouldBe(((DeductibilityType?)DeductibilityType.SpecialRule, (decimal?)null, (decimal?)null, true));
    }

    [Fact]
    public async Task Imports_into_a_closed_month_need_review_and_do_not_change_it()
    {
        _db.PfaAccountingPeriods.Add(new PfaAccountingPeriod { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Period = "2026-10", Status = AccountingPeriodStatus.Closed });
        await _db.SaveChangesAsync();
        Transaction(-300m, "OMV PETROM SA", null);

        await Import();

        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        (entry.Status, entry.ClosedPeriodFlag).ShouldBe((LedgerEntryStatus.NeedsReview, true));
        (await Verify(entry.Id)).Error.Code.ShouldBe("Accounting.PeriodClosed");
    }

    [Fact]
    public async Task Net_payouts_are_linked_to_the_platform_report_of_the_month()
    {
        Transaction(900m, "BOLT OPERATIONS OU", "Payout", new DateOnly(2026, 8, 10));
        Transaction(1100m, "BOLT OPERATIONS OU", "Payout", new DateOnly(2026, 9, 2));
        Guid report = Report(Platform.Bolt, income: 2500m, commission: 500m);

        IReadOnlyList<LedgerImportResult> results = await Import();

        (await _db.LedgerEntries.Where(e => e.PlatformDocumentId == report).CountAsync()).ShouldBe(2);
        (await _db.LedgerEntries.Where(e => e.TransactionType == LedgerTransactionType.Income).SumAsync(e => e.Amount)).ShouldBe(2000m);
        results.SelectMany(r => r.Notes).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unmatched_payouts_are_reported_not_linked()
    {
        Transaction(900m, "BOLT OPERATIONS OU", "Payout", new DateOnly(2026, 8, 10));
        Report(Platform.Bolt, income: 2500m, commission: 500m);

        IReadOnlyList<LedgerImportResult> results = await Import();

        results.SelectMany(r => r.Notes).ShouldHaveSingleItem().ShouldBe("Raport Bolt 08.2026: payout-urile din bancă (900,00 lei) nu dau netul din raport (2.000,00 lei).");
        (await _db.LedgerEntries.AnyAsync(e => e.PlatformDocumentId != null)).ShouldBeFalse();
    }

    [Fact]
    public async Task Gross_recognition_counts_the_report_once_and_payouts_as_transfers()
    {
        _options = new AccountingOptions { IncomeRecognition = LedgerIncomeRecognition.GrossReport };
        Transaction(2000m, "BOLT OPERATIONS OU", "Payout", new DateOnly(2026, 9, 2));
        Report(Platform.Bolt, income: 2500m, commission: 500m);

        await Import();
        await Import();

        List<LedgerEntry> entries = await _db.LedgerEntries.ToListAsync();
        entries.Select(e => (e.TransactionType, e.Amount, e.Source)).ShouldBe(
        [
            (LedgerTransactionType.Transfer, 2000m, LedgerSource.Bolt),
            (LedgerTransactionType.Income, 2500m, LedgerSource.Bolt),
            (LedgerTransactionType.Expense, -500m, LedgerSource.Bolt),
        ], ignoreOrder: true);
        entries.Where(e => e.TransactionType == LedgerTransactionType.Income).Sum(e => e.Amount).ShouldBe(2500m);
    }

    [Fact]
    public async Task Oblio_invoices_label_their_bank_payment_or_wait_for_review()
    {
        _db.OblioIntegrations.Add(new OblioIntegration { Id = Guid.NewGuid(), UserId = _user, ClientId = "ion@x.ro", ClientSecretEncrypted = "secret", Cif = "12345674", IsConnected = true });
        await _db.SaveChangesAsync();
        Transaction(500m, "FIRMA CLIENT SRL", "OP factura", new DateOnly(2026, 10, 12));
        _oblio.Invoices =
        [
            new OwnerInvoice("ION", "1", Day, null, "Firma Client SRL", "RO1", 500m, 500m, null, false),
            new OwnerInvoice("ION", "2", Day, null, "Client Numerar", null, 150m, 150m, null, false),
            new OwnerInvoice("ION", "3", Day, null, "Anulat", null, 90m, 90m, null, true),
        ];

        await Import();
        IReadOnlyList<LedgerImportResult> again = await Import();

        LedgerEntry bank = await _db.LedgerEntries.SingleAsync(e => e.Amount == 500m);
        (bank.TransactionType, bank.DocumentLabel, bank.Status, bank.Source).ShouldBe((LedgerTransactionType.Income, "Factura ION 1", LedgerEntryStatus.AutoImported, LedgerSource.Bank));
        LedgerEntry cash = await _db.LedgerEntries.SingleAsync(e => e.Source == LedgerSource.Oblio);
        (cash.Amount, cash.PaymentMethod, cash.Status, cash.DocumentLabel).ShouldBe((150m, PaymentMethod.Cash, LedgerEntryStatus.NeedsReview, "Factura ION 2"));
        again.Sum(r => r.Created + r.Updated).ShouldBe(0);
    }

    [Fact]
    public async Task Update_needs_a_reason_and_recalculates_deductibility()
    {
        Transaction(-80m, "MAGAZIN X", null);
        await Import();
        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();

        (await Update(entry.Id, """{"category":"PHONE"}""", " ")).Error.Code.ShouldBe("Accounting.ReasonRequired");
        (await Update(entry.Id, """{"category":"NOPE"}""", "Categorie")).Error.Code.ShouldBe("Accounting.InvalidField");
        LedgerEntryDto updated = (await Update(entry.Id, """{"category":"PHONE","description":"Abonament Orange"}""", "Factura Orange")).Value;

        (updated.Category, updated.DeductibleAmount, updated.Description).ShouldBe(("PHONE", (decimal?)80m, "Abonament Orange"));
        AuditLog audit = await _db.AuditLogs.SingleAsync(a => a.Action == "UPDATE");
        (audit.Reason, audit.BeforeJson, audit.AfterJson).ShouldBe(("Factura Orange", "{\"category\":null,\"description\":\"MAGAZIN X\"}", "{\"category\":\"PHONE\",\"description\":\"Abonament Orange\"}"));
        (await Verify(entry.Id)).Value.Status.ShouldBe(LedgerEntryStatus.Verified);
    }

    [Fact]
    public async Task An_expense_needs_a_category_before_verification()
    {
        Transaction(-80m, "MAGAZIN X", null);
        await Import();

        (await Verify((await _db.LedgerEntries.SingleAsync()).Id)).Error.Code.ShouldBe("Accounting.CategoryRequired");
    }

    [Fact]
    public async Task Manual_entries_follow_the_sign_of_their_type()
    {
        var handler = new CreateManualLedgerEntryCommandHandler(_db, new FixedUser(_accountant));

        LedgerEntryDto expense = (await handler.Handle(new CreateManualLedgerEntryCommand(_pfa, new ManualLedgerEntryRequest(
            Day, "", "Service Auto", "Revizie", LedgerTransactionType.Expense, PaymentMethod.Cash, 400m, "CAR_SERVICE", "Bon pierdut, chitanță")), CancellationToken.None)).Value;
        Result<LedgerEntryDto> noReason = await handler.Handle(new CreateManualLedgerEntryCommand(_pfa, new ManualLedgerEntryRequest(
            Day, "", null, "X", LedgerTransactionType.Income, PaymentMethod.Cash, 10m, null, "")), CancellationToken.None);

        (expense.Amount, expense.Source, expense.Status, expense.DocumentLabel, expense.DeductibleAmount).ShouldBe((-400m, LedgerSource.Manual, LedgerEntryStatus.Verified, "Notă contabilă", (decimal?)400m));
        noReason.Error.Code.ShouldBe("Accounting.ReasonRequired");
        (await _db.AuditLogs.SingleAsync()).Action.ShouldBe("CREATE_MANUAL");
    }

    [Fact]
    public async Task An_expense_document_proposes_its_bank_payment()
    {
        Transaction(-300m, "OMV PETROM SA", "Plata card", new DateOnly(2026, 10, 11));
        Transaction(-300m, "ALT COMERCIANT", "Plata card", new DateOnly(2026, 10, 30));
        await Import();
        _receipts.Expense = new ExpenseReceiptReading("OMV Petrom", "1590082", Day, 300m, ["Motorina"]);

        ExpenseDocumentUploadResult uploaded = (await new UploadExpenseDocumentCommandHandler(_db, Files(), _receipts, new FixedUser(_accountant), Options.Create(_options))
            .Handle(new UploadExpenseDocumentCommand(_pfa, new LedgerUpload("bon.jpg", "image/jpeg", [1, 2, 3])), CancellationToken.None)).Value;

        uploaded.Extracted.ShouldBe(new ExpenseDocumentExtracted("OMV Petrom", "1590082", Day, 300m, ["Motorina"]), new ExtractedComparer());
        uploaded.ProposedMatch!.Counterparty.ShouldBe("OMV PETROM SA");

        LedgerEntryDto confirmed = (await Update(uploaded.ProposedMatch.Id, $$"""{"sourceDocumentId":"{{uploaded.DocumentId}}"}""", "Bon OMV")).Value;
        confirmed.SourceDocumentId.ShouldBe(uploaded.DocumentId);
        (await _db.ExpenseDocuments.SingleAsync()).LedgerEntryId.ShouldBe(uploaded.ProposedMatch.Id);
        (await _db.Documents.SingleAsync(d => d.Id == uploaded.DocumentId)).Origin.ShouldBe(DocumentOrigin.AccountingUpload);
    }

    [Fact]
    public async Task Z_reports_need_an_active_cash_register_and_a_new_number()
    {
        _receipts.Z = new ZReportReading(new DateOnly(2026, 8, 20), "10", 100m);
        (await UploadZ()).Error.Code.ShouldBe("Accounting.CashNotActive");

        _receipts.Z = new ZReportReading(Day, "125", 420m);
        (await UploadZ()).IsSuccess.ShouldBeTrue();
        (await UploadZ()).Error.Description.ShouldBe("Raportul Z nr. 125 e deja înregistrat.");

        _receipts.Z = new ZReportReading(Day, null, 420m);
        (await UploadZ()).Error.Code.ShouldBe("Accounting.ZReportUnreadable");
        (await new UploadZReportCommandHandler(_db, Files(), _receipts, new FixedUser(_accountant), Options.Create(_options))
            .Handle(new UploadZReportCommand(_pfa, new LedgerUpload("z.txt", "text/plain", [1])), CancellationToken.None)).Error.Code.ShouldBe("Accounting.UploadFileType");
    }

    [Fact]
    public async Task Ledger_list_filters_and_pages()
    {
        Transaction(-300m, "OMV PETROM SA", null);
        Transaction(-49m, "MAGAZIN X", null);
        Transaction(1850m, "BOLT OPERATIONS OU", null);
        await Import();

        (await List(status: LedgerEntryStatus.NeedsReview)).Items.ShouldHaveSingleItem().Amount.ShouldBe(-49m);
        (await List(source: LedgerSource.Bolt)).Items.ShouldHaveSingleItem().Amount.ShouldBe(1850m);
        Paged<LedgerEntryDto> page = await List(pageSize: 2);
        (page.Total, page.Items.Count, page.PageSize).ShouldBe((3, 2, 2));
    }

    // ─── Ajutoare ──────────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<LedgerImportResult>> Import()
    {
        ILedgerSource[] sources =
        [
            new BankLedgerSource(_db),
            new PlatformLedgerSource(_db),
            new OblioLedgerSource(_db, new OwnerOblioResolver(_db, new PlainSecrets()), _oblio),
        ];
        return (await new RunLedgerImportCommandHandler(_db, sources, Options.Create(_options))
            .Handle(new RunLedgerImportCommand(_pfa), CancellationToken.None)).Value;
    }

    private Task<Result<ZReportUploadResult>> UploadZ() =>
        new UploadZReportCommandHandler(_db, Files(), _receipts, new FixedUser(_accountant), Options.Create(_options))
            .Handle(new UploadZReportCommand(_pfa, new LedgerUpload("z.pdf", "application/pdf", "%PDF z"u8.ToArray())), CancellationToken.None);

    private Task<Result<LedgerEntryDto>> Update(Guid id, string fields, string reason) =>
        new UpdateLedgerEntryCommandHandler(_db, new FixedUser(_accountant))
            .Handle(new UpdateLedgerEntryCommand(id, JsonDocument.Parse(fields).RootElement, reason), CancellationToken.None);

    private Task<Result<LedgerEntryDto>> Verify(Guid id) =>
        new VerifyLedgerEntryCommandHandler(_db, new FixedUser(_accountant)).Handle(new VerifyLedgerEntryCommand(id), CancellationToken.None);

    private async Task<Paged<LedgerEntryDto>> List(LedgerEntryStatus? status = null, LedgerSource? source = null, int pageSize = 25) =>
        (await new ListLedgerQueryHandler(_db).Handle(new ListLedgerQuery(_pfa, null, null, status, null, source, 1, pageSize), CancellationToken.None)).Value;

    private DeclarationFiles Files() => new(_db, new AnafDeclarationXmlService(), _files, new PlainSecrets());

    private void Transaction(decimal amount, string counterparty, string? details, DateOnly? date = null)
    {
        _db.BankTransactions.Add(new BankTransaction
        {
            Id = Guid.NewGuid(),
            BankAccountId = _account,
            UserId = _user,
            ProviderTransactionId = Guid.NewGuid().ToString("N"),
            BookingDate = date ?? Day,
            Amount = amount,
            Currency = "RON",
            CounterpartyName = counterparty,
            RemittanceInfo = details,
            ImportedAtUtc = DateTime.UtcNow,
        });
        _db.SaveChanges();
    }

    private Guid Report(Platform platform, decimal income, decimal commission)
    {
        var file = new Document { Id = Guid.NewGuid(), OriginalFileName = "raport.pdf", ContentType = "application/pdf", Origin = DocumentOrigin.AccountingUpload };
        var document = new PlatformDocument
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = _pfa,
            Period = "2026-08",
            Platform = platform,
            DocumentType = PlatformDocumentType.PlatformReport,
            SourceDocumentId = file.Id,
            FileHash = Guid.NewGuid().ToString("N"),
            Status = PlatformDocumentStatus.Confirmed,
        };
        _db.Documents.Add(file);
        _db.PlatformDocuments.Add(document);
        _db.DocumentExtractions.Add(new DocumentExtraction
        {
            Id = Guid.NewGuid(),
            PlatformDocumentId = document.Id,
            Version = 1,
            IsCurrent = true,
            PeriodFrom = new DateOnly(2026, 8, 1),
            PeriodTo = new DateOnly(2026, 8, 31),
            Currency = "RON",
            Amount = income,
            CommissionAmount = commission,
        });
        _db.SaveChanges();
        return document.Id;
    }

    private static ExpenseCategoryRule Category(string code, bool vehicle, DeductibilityType deductibility, string? pattern) => new()
    {
        Id = Guid.NewGuid(),
        Category = code,
        Label = code,
        VehicleRelated = vehicle,
        DefaultDeductibility = deductibility,
        CounterpartyPattern = pattern,
        ValidFrom = new DateOnly(2025, 1, 1),
    };

    private void VehicleDeductibility(DateOnly from, string value) => _db.PfaAccountingSettings.Add(new PfaAccountingSetting
    {
        Id = Guid.NewGuid(),
        PfaRegistrationId = _pfa,
        Key = PfaAccountingSettingKeys.VehicleDeductibility,
        ValueJson = $"\"{value}\"",
        ValidFrom = from,
        Note = "Setare",
        ChangedByUserId = _accountant,
    });

    private sealed class ExtractedComparer : IEqualityComparer<ExpenseDocumentExtracted>
    {
        public bool Equals(ExpenseDocumentExtracted? x, ExpenseDocumentExtracted? y) =>
            x is not null && y is not null && x with { Items = [] } == y with { Items = [] } && x.Items.SequenceEqual(y.Items);

        public int GetHashCode(ExpenseDocumentExtracted obj) => obj.Merchant?.GetHashCode(StringComparison.Ordinal) ?? 0;
    }

    private sealed class FakeReceipts : IReceiptExtractor
    {
        public ExpenseReceiptReading? Expense { get; set; }

        public ZReportReading? Z { get; set; }

        public Task<Result<ExpenseReceiptReading>> ReadExpenseAsync(ReceiptExtractionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Expense is null ? Result.Failure<ExpenseReceiptReading>(Error.Failure("Ai.RequestFailed", "x")) : Result.Success(Expense));

        public Task<Result<ZReportReading>> ReadZReportAsync(ReceiptExtractionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Z is null ? Result.Failure<ZReportReading>(Error.Failure("Ai.RequestFailed", "x")) : Result.Success(Z));
    }

    private sealed class FakeOblio : IOwnerInvoicingService
    {
        public IReadOnlyList<OwnerInvoice> Invoices { get; set; } = [];

        public Task<IReadOnlyList<OwnerInvoice>> ListInvoicesAsync(OwnerOblioCredentials credentials, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult(Invoices);

        public Task<OblioConnectionInfo> TestConnectionAsync(OwnerOblioCredentials credentials, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CollectAsync(OwnerOblioCredentials credentials, string seriesName, string number, decimal amountLei, string paymentMethod, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OwnerInvoice> CreateInvoiceAsync(OwnerOblioCredentials credentials, NewOwnerInvoice invoice, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CancelAsync(OwnerOblioCredentials credentials, string seriesName, string number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
