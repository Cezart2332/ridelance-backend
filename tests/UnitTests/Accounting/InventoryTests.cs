using Application.Abstractions.Authentication;
using Application.Accounting.Contracts;
using Application.Accounting.Inventory;
using Application.Accounting.Ledger;
using Application.Accounting.Registers;
using ClosedXML.Excel;
using Domain.Accounting;
using Domain.Banking;
using Domain.PfaRegistrations;
using Domain.Taxes;
using Domain.Users;
using Infrastructure.Accounting;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>Spec registre §5: inventarierea precompletată, confirmarea PFA-ului, revizuirea și finalizarea Adminului.</summary>
public sealed class InventoryTests : IDisposable
{
    private static readonly DateOnly YearEnd = new(2026, 12, 31);

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Guid _pfa = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _account = Guid.NewGuid();

    public InventoryTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var user = new User { Id = _user, Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu" };
        _db.Users.Add(new User { Id = _admin, Email = "admin@ridelance.ro", FirstName = "Admin", LastName = "RIDElance" });
        _db.PfaRegistrations.Add(new PfaRegistration { Id = _pfa, UserId = _user, User = user, FullName = "Ion Popescu", LegalName = "POPESCU ION PFA", Cui = "12345674" });

        // Numerarul calculat: 1.500 încasat, 300 plătit → 1.200 (scenariul 6).
        Cash(new DateOnly(2026, 11, 3), 1500m, LedgerTransactionType.Income);
        Cash(new DateOnly(2026, 12, 5), -300m, LedgerTransactionType.Expense);
        Cash(new DateOnly(2027, 1, 5), -50m, LedgerTransactionType.Expense);

        // Contul: soldul raportat la 10.01.2027 e 8.000, cu 500 intrați după 31.12.
        var connection = new BankConnection { Id = Guid.NewGuid(), UserId = _user, Provider = "test", InstitutionId = "BT", ProviderConsentId = "c", Status = BankConnectionStatus.Linked };
        _db.BankConnections.Add(connection);
        _db.BankAccounts.Add(new BankAccount
        {
            Id = _account, BankConnectionId = connection.Id, UserId = _user, ProviderAccountId = "acc", IsActive = true,
            Iban = "RO49AAAA1B31007593840000", Balance = 8000m, BalanceDate = new DateOnly(2027, 1, 10),
        });
        _db.BankTransactions.Add(new BankTransaction
        {
            Id = Guid.NewGuid(), BankAccountId = _account, UserId = _user, ProviderConsentId = "c", ProviderTransactionId = "t1",
            BookingDate = new DateOnly(2027, 1, 4), Amount = 500m, Currency = "RON", ImportedAtUtc = DateTime.UtcNow,
        });

        // Scenariul 7: factura de 400 lei neplătită la 31.12; o obligație fiscală de plată.
        _db.EFacturaMessages.Add(new EFacturaMessage
        {
            Id = Guid.NewGuid(), PfaRegistrationId = _pfa, AnafMessageId = "1", Kind = EFacturaMessageKind.Received,
            InvoiceNumber = "SRV-12", IssueDate = new DateOnly(2026, 12, 20), SupplierName = "Service Auto SRL", TotalAmount = 400m,
        });
        _db.TaxObligations.Add(new TaxObligation
        {
            Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Type = TaxObligationType.Cass, PeriodYear = 2026, PeriodMonth = 5,
            AmountDue = 2430m, DueDate = new DateOnly(2027, 5, 25), Status = TaxObligationStatus.DePlata, CreatedByUserId = _admin,
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>§5: precompletarea din active, bancă, numerar și datorii; factura neplătită e datorie, nu în RJIP (S7).</summary>
    [Fact]
    public async Task S7_PrefillBringsCashBankAndUnpaidInvoicesAsDebts()
    {
        InventoryCountDto count = await Start();

        count.Status.ShouldBe(InventoryStatus.AwaitingPfaConfirmation);
        Item(count, InventoryCategory.Cash).SystemValue.ShouldBe(1200m);
        Item(count, InventoryCategory.Cash).RequiresConfirmation.ShouldBeTrue();
        (Item(count, InventoryCategory.Bank).SystemValue, Item(count, InventoryCategory.Bank).RequiresConfirmation).ShouldBe((7500m, true));
        count.Items.Where(i => i.Category == InventoryCategory.Debts).Select(i => (i.Description, i.SystemValue)).ShouldBe(
            [("Factura SRV-12 – Service Auto SRL", 400m), ("Obligație fiscală Cass 05.2026", 2430m)], ignoreOrder: true);
        (await new GetRjipQueryHandler(_db).Handle(new GetRjipQuery(_pfa, new DateOnly(2026, 12, 1), YearEnd), CancellationToken.None))
            .Value.Rows.ShouldAllBe(r => r.CashOut != 400m && r.BankOut != 400m);
        (await TryStart()).Error.Code.ShouldBe("Accounting.InventoryExists");
    }

    /// <summary>Scenariul 6: numerar calculat 1.200, declarat 1.150 → diferență −50, notă obligatorie înainte de Final.</summary>
    [Fact]
    public async Task S6_ACashDifferenceNeedsANoteBeforeTheAdminFinalizes()
    {
        InventoryCountDto count = await Start();
        var client = new FixedUser(_user);

        (await Submit(count.Id)).Error.Code.ShouldBe("Accounting.InventoryUnconfirmed");
        count = (await ClientItem(Item(count, InventoryCategory.Cash).Id, InventoryItemAction.Adjust, 1150m, null)).Value;
        foreach (InventoryItemDto item in count.Items.Where(i => i.Status == InventoryItemStatus.Prefilled))
        {
            count = (await ClientItem(item.Id, InventoryItemAction.Confirm, null, null)).Value;
        }

        Item(count, InventoryCategory.Cash).Difference.ShouldBe(-50m);
        count = (await Submit(count.Id)).Value;
        count.Status.ShouldBe(InventoryStatus.AwaitingAdminReview);
        (await ClientItem(Item(count, InventoryCategory.Cash).Id, InventoryItemAction.Confirm, null, null)).Error.Code.ShouldBe("Accounting.InventoryWrongStep");

        (await Finalize(count.Id)).Error.Code.ShouldBe("Accounting.InventoryNoteRequired");
        await new UpdateInventoryItemCommandHandler(_db, new FixedUser(_admin)).Handle(
            new UpdateInventoryItemCommand(_pfa, count.Id, Item(count, InventoryCategory.Cash).Id, InventoryItemAction.Note, null, "Bon de 50 lei nedescărcat", ByAdmin: true),
            CancellationToken.None);
        InventoryCountDto final = (await Finalize(count.Id)).Value;

        final.Status.ShouldBe(InventoryStatus.Final);
        final.FinalizedBy!.Name.ShouldBe("Admin RIDElance");
        (await ClientItem(Item(final, InventoryCategory.Cash).Id, InventoryItemAction.Confirm, null, null)).Error.Code.ShouldBe("Accounting.InventoryReadOnly");
        (await new GetClientInventoryQueryHandler(_db, client).Handle(new GetClientInventoryQuery(), CancellationToken.None)).Value!.Id.ShouldBe(final.Id);

        RegisterFile file = (await new ExportInventoryQueryHandler(_db, new RegisterExporter())
            .Handle(new ExportInventoryQuery(_pfa, 2026, RegisterFormat.Xlsx), CancellationToken.None)).Value;
        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        IXLWorksheet sheet = workbook.Worksheet(1);
        sheet.RowsUsed().Single(r => r.Cell(2).GetString() == "Total Numerar").Cell(3).GetValue<decimal>().ShouldBe(1150m);
        sheet.RowsUsed().Any(r => r.Cell(1).GetString().StartsWith("Situație precompletată", StringComparison.Ordinal)).ShouldBeFalse();
    }

    /// <summary>§5 pas 2: PFA-ul scoate un bun cu notă și adaugă unul pe care sistemul nu îl știe.</summary>
    [Fact]
    public async Task Pfa_RemovesWithANoteAndAddsWhatTheSystemDoesNotKnow()
    {
        InventoryCountDto count = await Start();

        (await ClientItem(Item(count, InventoryCategory.Bank).Id, InventoryItemAction.Remove, null, null)).Error.Code.ShouldBe("Accounting.InventoryNoteRequired");
        count = (await new ClientAddInventoryItemCommandHandler(_db, new FixedUser(_user), new AddInventoryItemCommandHandler(_db, new FixedUser(_user)))
            .Handle(new ClientAddInventoryItemCommand(count.Id, InventoryCategory.InventoryObjects, "Cameră bord", 450m, "Cumpărată cu bani personali"), CancellationToken.None)).Value;

        InventoryItemDto added = count.Items.Single(i => i.Description == "Cameră bord");
        (added.Status, added.ConfirmedValue, added.Difference).ShouldBe((InventoryItemStatus.AddedManually, (decimal?)450m, 450m));
        (await new ClientInventoryItemCommandHandler(_db, new FixedUser(Guid.NewGuid()), Update(Guid.NewGuid()))
            .Handle(new ClientInventoryItemCommand(count.Id, added.Id, InventoryItemAction.Confirm, null, null), CancellationToken.None)).Error.Code.ShouldBe("Accounting.NoPfa");
    }

    /// <summary>§5 pas 1: jobul de la 31.12 creează inventarierea o singură dată, doar la sfârșit de an.</summary>
    [Fact]
    public async Task YearEndJob_CreatesTheInventoryOnceOnDecember31()
    {
        _db.PfaAccountingEngagements.Add(new PfaAccountingEngagement { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, StartDate = new DateOnly(2026, 3, 1), Status = EngagementStatus.Active });
        await _db.SaveChangesAsync();
        var start = new StartInventoryCountCommandHandler(_db);

        (await Infrastructure.BackgroundJobs.YearEndInventoryJob.RunAsync(_db, start, new DateOnly(2026, 12, 30), CancellationToken.None)).ShouldBe(0);
        (await Infrastructure.BackgroundJobs.YearEndInventoryJob.RunAsync(_db, start, YearEnd, CancellationToken.None)).ShouldBe(1);
        (await Infrastructure.BackgroundJobs.YearEndInventoryJob.RunAsync(_db, start, new DateOnly(2027, 1, 2), CancellationToken.None)).ShouldBe(0);
        (await _db.InventoryCounts.SingleAsync()).Date.ShouldBe(YearEnd);
    }

    // ─── Ajutoare ──────────────────────────────────────────────────────────────────────────────

    private static InventoryItemDto Item(InventoryCountDto count, InventoryCategory category) => count.Items.First(i => i.Category == category);

    private async Task<InventoryCountDto> Start() => (await TryStart()).Value;

    private Task<Result<InventoryCountDto>> TryStart() =>
        new StartInventoryCountCommandHandler(_db, new FixedUser(_admin))
            .Handle(new StartInventoryCountCommand(_pfa, YearEnd, InventoryReason.YearEnd), CancellationToken.None);

    private UpdateInventoryItemCommandHandler Update(Guid user) => new(_db, new FixedUser(user));

    private async Task<Result<InventoryCountDto>> ClientItem(Guid itemId, InventoryItemAction action, decimal? value, string? note)
    {
        Guid countId = await _db.InventoryCounts.Select(c => c.Id).SingleAsync();
        return await new ClientInventoryItemCommandHandler(_db, new FixedUser(_user), Update(_user))
            .Handle(new ClientInventoryItemCommand(countId, itemId, action, value, note), CancellationToken.None);
    }

    private Task<Result<InventoryCountDto>> Submit(Guid countId) =>
        new ClientSubmitInventoryCommandHandler(_db, new FixedUser(_user), new SubmitInventoryCountCommandHandler(_db, new FixedUser(_user)))
            .Handle(new ClientSubmitInventoryCommand(countId), CancellationToken.None);

    private Task<Result<InventoryCountDto>> Finalize(Guid countId) =>
        new FinalizeInventoryCountCommandHandler(_db, new FixedUser(_admin)).Handle(new FinalizeInventoryCountCommand(_pfa, countId), CancellationToken.None);

    private void Cash(DateOnly date, decimal amount, LedgerTransactionType type) => _db.LedgerEntries.Add(new LedgerEntry
    {
        Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Date = date, DocumentLabel = "Raport Z", Source = LedgerSource.CashZ,
        Description = "Numerar", TransactionType = type, PaymentMethod = PaymentMethod.Cash, Amount = amount,
        Status = LedgerEntryStatus.Verified, AccountingPeriod = LedgerSupport.PeriodOf(date),
    });

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
