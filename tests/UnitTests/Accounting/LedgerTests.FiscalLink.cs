using Application.Abstractions.Ai;
using Application.Abstractions.Services;
using Application.Accounting.Contracts;
using Application.Accounting.Ledger;
using Application.Accounting.Periods;
using Application.Accounting.Registers;
using Application.FiscalLink;
using Domain.Accounting;
using Domain.FiscalLink;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

public sealed partial class LedgerTests
{
    [Fact]
    public async Task FiscalLinkReceiptsAndZReachTheRegistersExactlyOnce()
    {
        CashFeed feed = Feed(300);
        feed.Documents = feed.Documents with { Reports = [.. feed.Documents.Reports, .. feed.Documents.Reports] };
        await SyncCash(feed);
        await SyncCash(feed);

        (await _db.FiscalReceipts.CountAsync()).ShouldBe(1);
        (await _db.ZReports.CountAsync()).ShouldBe(1);
        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        (entry.PaymentMethod, entry.Amount, entry.ReconciliationStatus).ShouldBe((PaymentMethod.Cash, 300m, ReconciliationStatus.Matched));
        entry.SourceDocumentId.ShouldNotBeNull();
        (await RjipOf(Day, Day)).MonthTotals.Single().CashIn.ShouldBe(300m);
        (await RefNow()).Rows[0].Value.ShouldBe(300m);
        (await _db.FiscalLinkClients.SingleAsync()).LastSyncAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task FiscalLinkReusesAMatchingManuallyUploadedZ()
    {
        _receipts.Z = new ZReportReading(Day, "125", 300);
        await UploadZ();
        Guid originalEntry = (await _db.LedgerEntries.SingleAsync()).Id;
        await SyncCash(Feed(300));

        (await _db.LedgerEntries.SingleAsync()).Id.ShouldBe(originalEntry);
        (await _db.ZReports.SingleAsync()).RegisterSerial.ShouldBe("DT123456");
        (await _db.FiscalReceipts.SingleAsync()).ZReportId.ShouldNotBeNull();
        (await _db.ZReports.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task DifferentFiscalLinkTotalDoesNotOverwriteTheAccountantsZ()
    {
        _receipts.Z = new ZReportReading(Day, "125", 400);
        await UploadZ();
        await SyncCash(Feed(300));

        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        entry.Amount.ShouldBe(400);
        entry.ReconciliationStatus.ShouldBe(ReconciliationStatus.NeedsReview);
        (await _db.FiscalLinkClients.SingleAsync()).LastSyncError!.ShouldContain("diferă");
        (await _db.ZReports.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task FiscalLinkSupportsTwoRegistersWithTheSameZNumberAndDay()
    {
        CashFeed feed = Feed(300);
        feed.Documents = feed.Documents with
        {
            Receipts = [.. feed.Documents.Receipts, feed.Documents.Receipts[0] with { Serial = "DT654321", Total = 200 }],
            Reports = [.. feed.Documents.Reports, feed.Documents.Reports[0] with { Serial = "DT654321", Total = 200 }],
        };
        await SyncCash(feed);
        await SyncCash(feed);

        (await _db.ZReports.CountAsync()).ShouldBe(2);
        (await _db.LedgerEntries.SumAsync(e => e.Amount)).ShouldBe(500);
        (await _db.LedgerEntries.AllAsync(e => e.ReconciliationStatus == ReconciliationStatus.Matched)).ShouldBeTrue();
    }

    [Fact]
    public async Task LateFiscalLinkZDoesNotChangeAClosedMonthsRegisters()
    {
        _db.PfaAccountingPeriods.Add(new PfaAccountingPeriod { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Period = "2026-10", Status = AccountingPeriodStatus.Closed });
        await _db.SaveChangesAsync();
        await SyncCash(Feed(300));

        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        entry.ClosedPeriodFlag.ShouldBeTrue();
        entry.TaxableIncomeAmount.ShouldBe(0);
        (await RefNow()).Rows[0].Value.ShouldBe(0);
        (await _db.FiscalLinkClients.SingleAsync()).LastSyncError!.ShouldContain("luna este închisă");
    }

    [Fact]
    public async Task ReceiptsImportedLaterDoNotChangeALockedZ()
    {
        CashFeed feed = Feed(300);
        feed.Documents = feed.Documents with { Receipts = [] };
        await SyncCash(feed);
        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        entry.Status = LedgerEntryStatus.Locked;
        await _db.SaveChangesAsync();

        feed.Documents = feed.Documents with { Receipts = [new FiscalLinkCashReceipt("DT123456", "1", new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc), 200, "receipt-1")] };
        await SyncCash(feed);
        entry.ReconciliationStatus.ShouldBe(ReconciliationStatus.Matched);
        entry.Amount.ShouldBe(300);
    }

    [Fact]
    public async Task AFailedFiscalLinkSyncKeepsItsPreviousSuccessAndRecordsTheError()
    {
        CashFeed feed = Feed(300);
        await SyncCash(feed);
        DateTime? lastSuccess = (await _db.FiscalLinkClients.SingleAsync()).LastSyncAtUtc;
        feed.Failure = Error.Problem("FiscalLink.Unavailable", "FiscalLink indisponibil");
        await SyncCash(feed);

        FiscalLinkClient client = await _db.FiscalLinkClients.SingleAsync();
        client.LastSyncAtUtc.ShouldBe(lastSuccess);
        client.LastSyncError.ShouldBe("FiscalLink indisponibil");
        (await _db.LedgerEntries.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CashZAndOnlinePayoutReconcileWithoutDoubleCountingPlatformCash()
    {
        Guid report = Report(Platform.Bolt, 1000, 200);
        (await _db.DocumentExtractions.SingleAsync(e => e.PlatformDocumentId == report)).CashAmount = 300;
        await _db.SaveChangesAsync();
        CommissionInvoice(Platform.Bolt);
        Transaction(500, "BOLT OPERATIONS OU", "Payout", new DateOnly(2026, 8, 31));
        CashFeed feed = Feed(300);
        feed.Documents = feed.Documents with
        {
            Receipts = [feed.Documents.Receipts[0] with { IssuedAtUtc = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc) }],
            Reports = [feed.Documents.Reports[0] with { IssuedAtUtc = new DateTime(2026, 8, 10, 20, 0, 0, DateTimeKind.Utc) }],
        };
        await SyncCash(feed);
        await Import();

        RefView fiscal = await RefNow();
        (fiscal.Rows[0].Value, fiscal.Rows[1].Value, fiscal.Rows[2].Value).ShouldBe((1000m, 200m, 800m));
        RjipMonthTotal totals = (await RjipOf(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31))).MonthTotals.Single();
        (totals.CashIn, totals.BankIn, totals.BankOut).ShouldBe((300m, 700m, 200m));
        IReadOnlyList<ReconciliationControlDto> controls = (await MonthOf("2026-08")).Controls;
        controls.Single(c => c.Control == ReconciliationControl.BankBalance).Passed.ShouldBeTrue();
        controls.Single(c => c.Control == ReconciliationControl.PlatformCashVsZ).Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task FiscalLinkOwnerStatusDoesNotExposeAnotherPfasDocuments()
    {
        CashFeed feed = Feed(300);
        await SyncCash(feed);
        var other = new GetFiscalLinkAccountingSyncQueryHandler(_db, new FixedUser(Guid.NewGuid()), feed);
        FiscalLinkAccountingSyncDto status = (await other.Handle(new GetFiscalLinkAccountingSyncQuery(), default)).Value;
        (status.Receipts, status.ZReports, status.LastSyncAtUtc).ShouldBe((0, 0, (DateTime?)null));
    }

    [Fact]
    public async Task FiscalLinkUsesRomanianDatesAndTheAccountingEngagementBounds()
    {
        CashFeed feed = Feed(300);
        _db.PfaAccountingEngagements.Add(new PfaAccountingEngagement { Id = Guid.NewGuid(), PfaRegistrationId = _pfa,
            StartDate = Day, EndDate = Day, Status = EngagementStatus.Inactive });
        await _db.SaveChangesAsync();
        // 9 October 22:00 UTC is 10 October in Romania and is included.
        var inside = new DateTime(2026, 10, 9, 22, 0, 0, DateTimeKind.Utc);
        feed.Documents = feed.Documents with
        {
            Receipts = [feed.Documents.Receipts[0] with { IssuedAtUtc = inside },
                feed.Documents.Receipts[0] with { Number = "2", IssuedAtUtc = inside.AddDays(1) }],
            Reports = [feed.Documents.Reports[0] with { IssuedAtUtc = inside },
                feed.Documents.Reports[0] with { Number = "126", IssuedAtUtc = inside.AddDays(1) }],
        };
        await SyncCash(feed);
        (await _db.FiscalReceipts.CountAsync()).ShouldBe(1);
        (await _db.ZReports.SingleAsync()).Date.ShouldBe(Day);
        (await _db.LedgerEntries.SingleAsync()).Amount.ShouldBe(300);
    }

    [Fact]
    public async Task AManualZCanUseTheSameNumberOnADifferentDayWithoutDuplicatingTheFirstDay()
    {
        _receipts.Z = new ZReportReading(Day, "125", 300);
        (await UploadZ()).IsSuccess.ShouldBeTrue();
        _receipts.Z = new ZReportReading(Day.AddDays(1), "125", 200);
        (await UploadZ()).IsSuccess.ShouldBeTrue();
        (await UploadZ()).IsFailure.ShouldBeTrue();
        (await _db.LedgerEntries.SumAsync(e => e.Amount)).ShouldBe(500);
    }

    [Fact]
    public async Task MissingZFromOneRegisterDoesNotPassBecauseAnotherRegisterHasAZ()
    {
        CashFeed feed = Feed(300);
        feed.Documents = feed.Documents with { Receipts = [.. feed.Documents.Receipts,
            feed.Documents.Receipts[0] with { Serial = "DT654321", Total = 200 }] };
        await SyncCash(feed);
        ReconciliationControlDto control = (await MonthOf("2026-10")).Controls.Single(c => c.Control == ReconciliationControl.CashRegister);
        control.Passed.ShouldBeFalse();
        control.Detail.ShouldContain("DT654321");
    }

    [Fact]
    public async Task TheFiscalLinkSyncButtonDoesNotImportUnrelatedBankTransactions()
    {
        CashFeed feed = Feed(300);
        Transaction(-80, "SERVICE AUTO", "Plată");
        var import = new RunLedgerImportCommandHandler(_db, [new BankLedgerSource(_db), new FiscalLinkLedgerSource(_db, feed, Files())], Options.Create(_options));
        var handler = new SyncFiscalLinkAccountingCommandHandler(_db, new FixedUser(_user), feed, import);
        (await handler.Handle(new SyncFiscalLinkAccountingCommand(), default)).IsSuccess.ShouldBeTrue();
        (await _db.LedgerEntries.CountAsync()).ShouldBe(1);
        (await _db.LedgerEntries.SingleAsync()).Source.ShouldBe(LedgerSource.CashZ);
    }

    [Fact]
    public async Task ADeletedOwnerCannotReadOrSynchronizeFiscalLink()
    {
        CashFeed feed = Feed(300);
        await SyncCash(feed);
        (await _db.Users.SingleAsync(u => u.Id == _user)).DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        FiscalLinkClient? own = await FiscalLinkAccountingAccess.OwnAsync(_db, _user, default);
        own.ShouldBeNull();
    }

    private CashFeed Feed(decimal total)
    {
        if (!_db.FiscalLinkClients.Any())
        {
            _db.FiscalLinkClients.Add(new FiscalLinkClient { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, UserId = _user, FiscalLinkClientId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow });
            _db.SaveChanges();
        }
        var utc = new DateTime(2026, 10, 10, 20, 0, 0, DateTimeKind.Utc);
        return new CashFeed(new FiscalLinkCashDocuments(
            [new FiscalLinkCashReceipt("DT123456", "1", utc, total, "receipt-1")],
            [new FiscalLinkCashZ("DT123456", "125", utc, total, "z-125", "{\"reportNumber\":125}")], []));
    }

    private async Task SyncCash(CashFeed feed)
    {
        var source = new FiscalLinkLedgerSource(_db, feed, Files());
        (await new RunLedgerImportCommandHandler(_db, [source], Options.Create(_options))
            .Handle(new RunLedgerImportCommand(_pfa), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    private sealed class CashFeed(FiscalLinkCashDocuments documents) : IFiscalLinkAccountingService
    {
        public bool IsConfigured => true;
        public FiscalLinkCashDocuments Documents { get; set; } = documents;
        public Error? Failure { get; set; }
        public Task<Result<FiscalLinkCashDocuments>> ReadAsync(Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Failure is { } error ? Result.Failure<FiscalLinkCashDocuments>(error) : Result.Success(Documents));
    }
}
