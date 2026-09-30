using Application.Accounting.Ledger;
using Domain.Accounting;
using Domain.Banking;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>Invarianții ledger-ului (spec flux contabil §4), în domeniu și la salvare.</summary>
public sealed class LedgerInvariantTests
{
    private static LedgerEntry Entry(decimal amount, decimal personal = 0, decimal? deductible = null, Guid? group = null, Guid? bank = null) => new()
    {
        Id = Guid.NewGuid(),
        PfaRegistrationId = Guid.NewGuid(),
        Date = new DateOnly(2026, 9, 15),
        Amount = amount,
        PersonalAmount = personal,
        DeductibleAmount = deductible,
        SettlementGroupId = group,
        BankTransactionId = bank,
        TransactionType = amount > 0 ? LedgerTransactionType.Income : LedgerTransactionType.Expense,
        DocumentLabel = "Extras 15.09.2026",
        Description = "Test",
        AccountingPeriod = "2026-09",
    };

    [Fact]
    public void Invariant_BusinessPlusPersonalIsTheAmount()
    {
        LedgerEntry entry = Entry(-250, personal: 50);

        entry.BusinessAmount.ShouldBe(200);
        (entry.BusinessAmount + entry.PersonalAmount).ShouldBe(Math.Abs(entry.Amount));
        LedgerInvariants.Check(entry).ShouldBeEmpty();
        LedgerInvariants.Check(Entry(-250, personal: 300)).ShouldNotBeEmpty();
        LedgerInvariants.Check(Entry(-250, personal: -1)).ShouldNotBeEmpty();
    }

    [Fact]
    public void R30_PersonalPartIsNeverDeductible()
    {
        // 250 = 200 carburant + 50 personal: deductibilul încape doar în cei 200.
        LedgerInvariants.Check(Entry(-250, personal: 50, deductible: 200)).ShouldBeEmpty();
        LedgerInvariants.Check(Entry(-250, personal: 50, deductible: 250)).ShouldNotBeEmpty();

        LedgerEntry fuel = Entry(-250, personal: 50, deductible: 100);
        (fuel.DeductibleAmount + fuel.NonDeductibleAmount).ShouldBe(250);
        fuel.NonDeductibleAmount.ShouldBe(150); // 100 nedeductibil din partea business + 50 personal
    }

    [Fact]
    public void R30_DeductibilityIsComputedOnTheBusinessPart()
    {
        LedgerEntry fuel = Entry(-250, personal: 50);
        fuel.Category = "FUEL";
        var rule = new ExpenseCategoryRule
        {
            Id = Guid.NewGuid(), Category = "FUEL", VehicleRelated = false,
            DefaultDeductibility = DeductibilityType.Percent100, ValidFrom = new DateOnly(2026, 1, 1),
        };

        DeductibilityService.Resolve(fuel, new LedgerRules([rule], []));

        fuel.DeductibleAmount.ShouldBe(200);
        LedgerInvariants.Check(fuel).ShouldBeEmpty();
    }

    [Fact]
    public void Invariant_AmountsHaveTwoDecimalsRoundedAwayFromZero()
    {
        LedgerInvariants.Round(2.345m).ShouldBe(2.35m);
        LedgerInvariants.Round(-2.345m).ShouldBe(-2.35m);
        LedgerInvariants.Check(Entry(-10.005m)).ShouldNotBeEmpty();
    }

    [Fact]
    public void R21_OneBankTransactionSplitsOnlyInsideOneSettlementGroupThatNetsToIt()
    {
        var group = Guid.NewGuid();
        var bank = Guid.NewGuid();

        // Exemplul din spec: online 5.000, comision 350, payout 4.650.
        LedgerInvariants.CheckBankLinks(4650, [Entry(5000, group: group, bank: bank), Entry(-350, group: group, bank: bank)]).ShouldBeEmpty();

        LedgerInvariants.CheckBankLinks(4650, [Entry(5000, group: group, bank: bank), Entry(-300, group: group, bank: bank)])
            .ShouldHaveSingleItem().ShouldContain("4700");
        LedgerInvariants.CheckBankLinks(4650, [Entry(5000, bank: bank), Entry(-350, bank: bank)]).ShouldNotBeEmpty();
        LedgerInvariants.CheckBankLinks(4650, [Entry(5000, group: group, bank: bank), Entry(-350, group: Guid.NewGuid(), bank: bank)]).ShouldNotBeEmpty();
        LedgerInvariants.CheckBankLinks(-250, [Entry(-250, bank: bank)]).ShouldBeEmpty();
    }

    [Fact]
    public void R04b_PartialPaymentsCannotExceedTheInvoice()
    {
        LedgerInvariants.CheckPayments(1200, [-500, -700]).ShouldBeEmpty();
        LedgerInvariants.CheckPayments(1200, [-500]).ShouldBeEmpty();
        LedgerInvariants.CheckPayments(1200, [-500, -800]).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task SavingAnEntryThatBreaksAnInvariantIsRefused()
    {
        await using ApplicationDbContext db = Database();
        (Guid pfa, BankTransaction transaction) = await SeedAsync(db, -250);

        LedgerEntry personalTooBig = Entry(-250, personal: 300, bank: transaction.Id);
        personalTooBig.PfaRegistrationId = pfa;
        db.LedgerEntries.Add(personalTooBig);
        await Should.ThrowAsync<LedgerInvariantException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        LedgerEntry wrongSum = Entry(-200, bank: transaction.Id);
        wrongSum.PfaRegistrationId = pfa;
        db.LedgerEntries.Add(wrongSum);
        (await Should.ThrowAsync<LedgerInvariantException>(() => db.SaveChangesAsync())).Message.ShouldContain("-200.00");
        db.ChangeTracker.Clear();

        LedgerEntry fine = Entry(-250, personal: 50, deductible: 200, bank: transaction.Id);
        fine.PfaRegistrationId = pfa;
        db.LedgerEntries.Add(fine);
        await db.SaveChangesAsync();

        // A doua înregistrare pe aceeași tranzacție, fără grup de decontare: refuzată.
        LedgerEntry second = Entry(-250, bank: transaction.Id);
        second.PfaRegistrationId = pfa;
        db.LedgerEntries.Add(second);
        await Should.ThrowAsync<LedgerInvariantException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task R21_TheSettlementSplitIsSavedWhenItNetsToThePayout()
    {
        await using ApplicationDbContext db = Database();
        (Guid pfa, BankTransaction payout) = await SeedAsync(db, 4650);
        var group = Guid.NewGuid();

        LedgerEntry gross = Entry(5000, group: group, bank: payout.Id);
        LedgerEntry commission = Entry(-350, group: group, bank: payout.Id);
        gross.PfaRegistrationId = commission.PfaRegistrationId = pfa;
        db.LedgerEntries.AddRange(gross, commission);
        await db.SaveChangesAsync();

        (await db.LedgerEntries.CountAsync(e => e.BankTransactionId == payout.Id)).ShouldBe(2);
    }

    private static async Task<(Guid Pfa, BankTransaction Transaction)> SeedAsync(ApplicationDbContext db, decimal amount)
    {
        var user = new User { Id = Guid.NewGuid(), Email = "ion@example.test", FirstName = "Ion", LastName = "Popescu" };
        var pfa = new PfaRegistration { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Ion Popescu" };
        var connection = new BankConnection { Id = Guid.NewGuid(), UserId = user.Id, Provider = "test", InstitutionId = "BT", ProviderConsentId = "c", Status = BankConnectionStatus.Linked };
        var account = new BankAccount { Id = Guid.NewGuid(), BankConnectionId = connection.Id, UserId = user.Id, ProviderAccountId = "acc", IsActive = true };
        var transaction = new BankTransaction
        {
            Id = Guid.NewGuid(), BankAccountId = account.Id, UserId = user.Id, ProviderTransactionId = "tx-1",
            BookingDate = new DateOnly(2026, 9, 15), Amount = amount, Currency = "RON", ImportedAtUtc = DateTime.UtcNow,
        };
        db.Users.Add(user);
        db.PfaRegistrations.Add(pfa);
        db.BankConnections.Add(connection);
        db.BankAccounts.Add(account);
        db.BankTransactions.Add(transaction);
        await db.SaveChangesAsync();
        return (pfa.Id, transaction);
    }

    private static ApplicationDbContext Database() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Events());

    private sealed class Events : Infrastructure.DomainEvents.IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
