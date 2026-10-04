using System.Text.Json;
using Application.Accounting.Contracts;
using Application.Accounting.FiscalRegister;
using Application.Accounting.Ledger;
using Application.Accounting.Periods;
using Application.Accounting.Registers;
using Domain.Accounting;
using Domain.Banking;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// Raportul QA din browser, reprodus pe un seed local (fără date din producție): PFA cu titularul
/// „Ionescu Andrei-Victor”, transferurile către titular, ghiseul.ro, comisionul lunar al băncii,
/// Booking.com, rapoartele Bolt/Uber din august cu payout-urile lor.
/// </summary>
public sealed partial class LedgerTests
{
    private static readonly DateOnly July10 = new(2026, 7, 10);

    /// <summary>Seed-ul QA (cifrele din raport).</summary>
    private void SeedQa(bool payouts = true)
    {
        Domain.PfaRegistrations.PfaRegistration pfa = _db.PfaRegistrations.Single(p => p.Id == _pfa);
        pfa.FullName = "Victor Ionescu";
        pfa.HolderName = "IONESCU ANDREI-VICTOR";
        Domain.Users.User user = _db.Users.Single(u => u.Id == _user);
        (user.FirstName, user.LastName) = ("testr", "test");
        _db.SaveChanges();

        Transaction(-200m, null, "To Victor Ionescu", July10);
        Transaction(-50m, null, "Company Free plan fee", new DateOnly(2026, 7, 15));
        Transaction(630m, "Victor Ionescu", "From Victor I", new DateOnly(2026, 7, 25));
        Transaction(-567m, null, "www.ghiseul.ro/mfinante", new DateOnly(2026, 7, 25));
        Transaction(-600m, null, "To Victor Ionescu", new DateOnly(2026, 8, 5));
        Transaction(-50m, null, "Company Free plan fee", new DateOnly(2026, 8, 15));
        Transaction(-100m, null, "To Victor Ionescu", new DateOnly(2026, 9, 5));
        Transaction(-50m, null, "Company Free plan fee", new DateOnly(2026, 9, 15));
        Transaction(822.28m, "Booking.com Bv", "Payment from Booking.com Bv", new DateOnly(2026, 9, 15));

        // August: Bolt 20.758,20 (comision 2.273,23), Uber 4.173,86 (comision 557,31), cu payout-urile nete.
        Report(Platform.Bolt, 20758.20m, 2273.23m);
        Report(Platform.Uber, 4173.86m, 557.31m);
        CommissionInvoice(Platform.Bolt);
        CommissionInvoice(Platform.Uber);
        if (payouts)
        {
            Transaction(18484.97m, "BOLT OPERATIONS OU", "Payout august", new DateOnly(2026, 8, 31));
            Transaction(3616.55m, "UBER BV", "Payout august", new DateOnly(2026, 8, 31));
        }
    }

    private Task<RefView> RefNow() => GetRefQueryHandler.ComputeAsync(_db, _pfa, 2026, RefStatus.Current, null, CancellationToken.None);

    private async Task<RjipView> RjipOf(DateOnly from, DateOnly to) =>
        (await new GetRjipQueryHandler(_db).Handle(new GetRjipQuery(_pfa, from, to), CancellationToken.None)).Value;

    private async Task<MonthReconciliationDto> MonthOf(string period) =>
        (await new GetMonthReconciliationQueryHandler(_db).Handle(new GetMonthReconciliationQuery(_pfa, period), CancellationToken.None)).Value;

    /// <summary>
    /// QA 1: „To Victor Ionescu” 200 lei clasificat din Bancă ca Service auto și verificat. Aceeași clasificare
    /// e în RJIP, în excepții și în REF: fără document e „Document lipsă” și nu se deduce (R01); cu documentul
    /// atașat, REF include partea deductibilă.
    /// </summary>
    [Fact]
    public async Task QA1_ABankScreenClassificationIsTheSameEverywhere()
    {
        SeedQa();
        await Import();
        LedgerEntry entry = await _db.LedgerEntries.SingleAsync(e => e.Date == July10);
        decimal deductibleBefore = (await RefNow()).Rows[1].Value;

        (await Update(entry.Id, """{"category":"CAR_SERVICE"}""", "Clasificat din Bancă")).IsSuccess.ShouldBeTrue();
        (await Verify(entry.Id)).IsSuccess.ShouldBeTrue();

        entry = await _db.LedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        (entry.TransactionType, entry.Category, entry.ProposedClassification, entry.ReconciliationStatus)
            .ShouldBe((LedgerTransactionType.Expense, "CAR_SERVICE", (BankClassification?)null, ReconciliationStatus.Unmatched));
        RjipRow row = (await RjipOf(July10, July10)).Rows.ShouldHaveSingleItem();
        (row.Operation, row.Exception).ShouldBe(("CAR_SERVICE", (RegisterExceptionKind?)RegisterExceptionKind.MissingDocument));
        (await RefNow()).Rows[1].Value.ShouldBe(deductibleBefore);

        var file = new Document { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, OriginalFileName = "factura.pdf", ContentType = "application/pdf", Origin = DocumentOrigin.AccountingUpload };
        _db.Documents.Add(file);
        await _db.SaveChangesAsync();
        (await Update(entry.Id, JsonSerializer.Serialize(new { sourceDocumentId = file.Id }), "Factura service")).IsSuccess.ShouldBeTrue();

        entry = await _db.LedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        entry.ReconciliationStatus.ShouldBe(ReconciliationStatus.Matched);
        (await RjipOf(July10, July10)).Rows.ShouldHaveSingleItem().Exception.ShouldBeNull();
        (await RefNow()).Rows[1].Value.ShouldBe(deductibleBefore + entry.DeductibleAmount!.Value);
        entry.DeductibleAmount.ShouldBe(200m);
    }

    /// <summary>QA 7: cu titularul „Ionescu Andrei-Victor”, transferurile 200/600/100 și „From Victor I” ajung la „Transferuri de confirmat”.</summary>
    [Fact]
    public async Task QA7_OwnerTransfersBecomeTransfersToConfirm()
    {
        SeedQa();
        await Import();

        RegisterExceptionsDto exceptions = (await new GetRegisterExceptionsQueryHandler(_db).Handle(new GetRegisterExceptionsQuery(_pfa, 2026), CancellationToken.None)).Value;
        RegisterExceptionGroupDto transfers = exceptions.Groups.Single(g => g.Kind == RegisterExceptionKind.TransferToConfirm);
        transfers.Items.Select(i => (i.Amount, i.Proposal!.Classification)).ShouldBe(
        [
            (-200m, BankClassification.OwnerWithdrawal),
            (630m, BankClassification.OwnerContribution),
            (-600m, BankClassification.OwnerWithdrawal),
            (-100m, BankClassification.OwnerWithdrawal),
        ]);
    }

    /// <summary>QA 8: „Verifică” nu validează o încasare neidentificată sau o propunere neconfirmată.</summary>
    [Fact]
    public async Task QA8_VerifyRequiresAClassification()
    {
        SeedQa();
        await Import();

        LedgerEntry booking = await _db.LedgerEntries.SingleAsync(e => e.Amount == 822.28m);
        (await Verify(booking.Id)).Error.Code.ShouldBe("Accounting.ClassificationRequired");
        LedgerEntry contribution = await _db.LedgerEntries.SingleAsync(e => e.Amount == 630m);
        (await Verify(contribution.Id)).Error.Code.ShouldBe("Accounting.ClassificationRequired");

        await new ClassifyBankEntryCommandHandler(_db, new FixedUser(_accountant))
            .Handle(new ClassifyBankEntryCommand(contribution.Id, BankClassification.OwnerContribution, false), CancellationToken.None);
        (await Verify(contribution.Id)).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// QA 2: raportul Bolt 20.758,20 (comision 2.273,23) și Uber 4.173,86 (comision 557,31), cu payout-urile din
    /// bancă, devin venit brut + comision cu același grup de decontare, la data decontării; netul = payout-ul.
    /// </summary>
    [Fact]
    public async Task QA2_PlatformReportsWithPayoutsBecomeIncomeAndCommission()
    {
        SeedQa();
        await Import();

        List<LedgerEntry> settled = await _db.LedgerEntries.Where(e => e.SettlementGroupId != null).ToListAsync();
        settled.Where(e => e.TransactionType == LedgerTransactionType.Income).Select(e => e.Amount).OrderBy(a => a).ShouldBe([4173.86m, 20758.20m]);
        settled.Where(e => e.TransactionType == LedgerTransactionType.Expense).Select(e => e.Amount).OrderBy(a => a).ShouldBe([-2273.23m, -557.31m]);
        settled.GroupBy(e => e.SettlementGroupId).Select(g => g.Sum(e => e.Amount)).OrderBy(a => a).ShouldBe([3616.55m, 18484.97m]);
        settled.ShouldAllBe(e => e.Date == new DateOnly(2026, 8, 31));

        RjipMonthTotal august = (await RjipOf(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31))).MonthTotals.ShouldHaveSingleItem();
        august.BankIn.ShouldBe(24932.06m);
        (await RefNow()).Rows[0].Value.ShouldBe(24932.06m);

        ReconciliationControlDto payouts = (await MonthOf("2026-08")).Controls.Single(c => c.Control == ReconciliationControl.UnreconciledPayouts);
        (payouts.Passed, payouts.Detail).ShouldBe((true, "Toate payout-urile sunt reconciliate."));
    }

    /// <summary>QA 2: rapoarte confirmate fără payout în bancă → nu „toate reconciliate”, venitul lipsește și luna nu se închide.</summary>
    [Fact]
    public async Task QA2_ConfirmedReportsWithoutPayoutsAreNotReconciled()
    {
        SeedQa(payouts: false);
        await Import();

        (await _db.LedgerEntries.AnyAsync(e => e.SettlementGroupId != null)).ShouldBeFalse();
        MonthReconciliationDto month = await MonthOf("2026-08");
        ReconciliationControlDto payouts = month.Controls.Single(c => c.Control == ReconciliationControl.UnreconciledPayouts);
        payouts.Passed.ShouldBeFalse();
        payouts.Detail.ShouldContain("raportul Bolt e confirmat, dar fără payout reconciliat în bancă");
        payouts.Detail.ShouldContain("raportul Uber e confirmat");
        month.CanClose.ShouldBeFalse();
    }

    /// <summary>
    /// QA 3: o tranzacție dintr-un cont inactiv (sau din alt consimțământ) nu e importată; controlul de sold
    /// folosește aceleași tranzacții ca importul, deci nu inventează o diferență.
    /// </summary>
    [Fact]
    public async Task QA3_TheBalanceCheckUsesTheSameTransactionsAsTheImport()
    {
        SeedQa();
        var inactive = Guid.NewGuid();
        _db.BankAccounts.Add(new BankAccount { Id = inactive, BankConnectionId = (await _db.BankConnections.FirstAsync()).Id, UserId = _user, ProviderAccountId = "vechi", IsActive = false });
        _db.BankTransactions.Add(new BankTransaction
        {
            Id = Guid.NewGuid(), BankAccountId = inactive, UserId = _user, ProviderConsentId = "consent-ledger",
            ProviderTransactionId = "inactiv", BookingDate = new DateOnly(2026, 7, 20), Amount = -142m, Currency = "RON", ImportedAtUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        await Import();

        ReconciliationControlDto balance = (await MonthOf("2026-07")).Controls.Single(c => c.Control == ReconciliationControl.BankBalance);
        (balance.Passed, balance.Detail).ShouldBe((true, "Variația contului -187,00 lei = RJIP bancă."));

        // O diferență reală (tranzacție neimportată încă) blochează închiderea lunii.
        Transaction(-95.01m, "Magazin", "neimportata", new DateOnly(2026, 8, 20));
        MonthReconciliationDto august = await MonthOf("2026-08");
        ReconciliationControlDto augustBalance = august.Controls.Single(c => c.Control == ReconciliationControl.BankBalance);
        augustBalance.Passed.ShouldBeFalse();
        augustBalance.Detail.ShouldEndWith("diferență -95,01 lei.");
        august.CanClose.ShouldBeFalse();
    }
}
