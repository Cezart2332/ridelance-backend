using Application.Accounting;
using Application.Accounting.FiscalRegister;
using Application.Accounting.Ledger;
using Application.Accounting.Registers;
using Domain.Accounting;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

public sealed partial class LedgerTests
{
    private DeclarationVersion D301(decimal amount = 3303.75m)
    {
        _db.PfaRegistrations.Single(p => p.Id == _pfa).AssignedContabilId = _accountant;
        _db.ExpenseCategoryRules.Add(Category(DeductibilityService.NonRecoverableVatCategory, false, DeductibilityType.Percent100, null));
        // A large tax payment must never be proposed as a fixed asset.
        _db.FixedAssetRules.Add(new FixedAssetRule { Id = Guid.NewGuid(), Threshold = 2500, ValidFrom = new DateOnly(2025, 1, 1) });
        var pdf = new Document { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, OriginalFileName = "D301.pdf", ContentType = "application/pdf", Origin = DocumentOrigin.AccountingGenerated };
        var declaration = new Declaration { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Period = "2026-08", Type = DeclarationType.D301 };
        var version = new DeclarationVersion { Id = Guid.NewGuid(), Declaration = declaration, DeclarationId = declaration.Id,
            VersionNo = 1, Amount = amount, PdfDocumentId = pdf.Id, Status = DeclarationStatus.Accepted };
        _db.Documents.Add(pdf);
        _db.Declarations.Add(declaration);
        _db.DeclarationVersions.Add(version);
        _db.SaveChanges();
        return version;
    }

    private Task<SharedKernel.Result<Application.Accounting.Contracts.LedgerEntryDto>> AssociateVat(Guid entry, Guid version, Guid? pfa = null, bool confirmed = true) =>
        new AssociateD301PaymentCommandHandler(_db, new FixedUser(_accountant)).Handle(
            new AssociateD301PaymentCommand(pfa ?? _pfa, entry, version, confirmed, "TVA nerecuperabil pe comisioane verificat"), CancellationToken.None);

    [Fact]
    public async Task D301_PaidInSeptember_EntersSeptemberRJIPAndAnnualREF_Once()
    {
        DeclarationVersion declaration = D301();
        Transaction(-3304, "ANAF", "D301 august", new DateOnly(2026, 9, 25), "RO49TREZ1234567891234567");
        await Import();
        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        entry.TransactionType.ShouldBe(LedgerTransactionType.Tax);
        (await new GetRefQueryHandler(_db).Handle(new GetRefQuery(_pfa, 2026), CancellationToken.None)).Value.Rows[1].Value.ShouldBe(0);

        (await AssociateVat(entry.Id, declaration.Id)).IsSuccess.ShouldBeTrue();
        (await AssociateVat(entry.Id, declaration.Id)).IsSuccess.ShouldBeTrue();
        (await _db.LedgerEntries.CountAsync()).ShouldBe(1);
        entry.Date.ShouldBe(new DateOnly(2026, 9, 25));
        entry.Amount.ShouldBe(-3304);
        entry.FixedAssetReview.ShouldBe(FixedAssetReview.None);
        entry.DeductibleAmount.ShouldBe(3304);
        entry.SourceDocumentId.ShouldBe(declaration.PdfDocumentId);
        Application.Accounting.Contracts.RjipView rjip = (await new GetRjipQueryHandler(_db).Handle(new GetRjipQuery(_pfa, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), CancellationToken.None)).Value;
        rjip.Rows.ShouldHaveSingleItem().BankOut.ShouldBe(3304);
        Application.Accounting.Contracts.RefView fiscal = (await new GetRefQueryHandler(_db).Handle(new GetRefQuery(_pfa, 2026), CancellationToken.None)).Value;
        fiscal.Rows[1].Value.ShouldBe(3304);
        (await _db.AuditLogs.CountAsync(a => a.Action == "ASSOCIATE_D301_PAYMENT")).ShouldBe(1);
    }

    [Fact]
    public async Task D301_DeclarationAloneAndOtherANAFTaxes_DoNotCreateDeductibleExpenses()
    {
        D301();
        await Import();
        (await _db.LedgerEntries.CountAsync()).ShouldBe(0);
        Transaction(-500, "ANAF", "Impozit pe venit", iban: "RO49TREZ1234567891234567");
        await Import();
        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        entry.TransactionType.ShouldBe(LedgerTransactionType.Tax);
        entry.DeductibleAmount.ShouldBeNull();
        (await new GetRefQueryHandler(_db).Handle(new GetRefQuery(_pfa, 2026), CancellationToken.None)).Value.Rows[1].Value.ShouldBe(0);
    }

    [Fact]
    public async Task D301_PartialPaymentsCannotExceedTheObligation()
    {
        DeclarationVersion declaration = D301(1000);
        Transaction(-600, "ANAF", "D301 prima plata", iban: "RO49TREZ1234567891234567");
        Transaction(-400, "ANAF", "D301 rest", iban: "RO49TREZ1234567891234567");
        Transaction(-1, "ANAF", "Alta taxa", iban: "RO49TREZ1234567891234567");
        await Import();
        List<LedgerEntry> entries = await _db.LedgerEntries.OrderBy(e => e.Amount).ToListAsync();
        (await AssociateVat(entries[0].Id, declaration.Id)).IsSuccess.ShouldBeTrue();
        (await AssociateVat(entries[1].Id, declaration.Id)).IsSuccess.ShouldBeTrue();
        (await AssociateVat(entries[2].Id, declaration.Id)).IsFailure.ShouldBeTrue();
        entries[2].TransactionType.ShouldBe(LedgerTransactionType.Tax);
    }

    [Fact]
    public async Task D301_RequiresOwnDeclarationExplicitConfirmationAndOpenPaymentPeriod()
    {
        DeclarationVersion declaration = D301();
        Transaction(-3304, "ANAF", "D301", iban: "RO49TREZ1234567891234567");
        await Import();
        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        (await AssociateVat(entry.Id, declaration.Id, confirmed: false)).IsFailure.ShouldBeTrue();
        (await AssociateVat(entry.Id, declaration.Id, pfa: Guid.NewGuid())).IsFailure.ShouldBeTrue();
        declaration.Declaration.PfaRegistrationId = Guid.NewGuid();
        await _db.SaveChangesAsync();
        (await AssociateVat(entry.Id, declaration.Id)).IsFailure.ShouldBeTrue();
        declaration.Declaration.PfaRegistrationId = _pfa;
        _db.PfaAccountingPeriods.Add(new PfaAccountingPeriod { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Period = entry.AccountingPeriod, Status = AccountingPeriodStatus.Closed });
        await _db.SaveChangesAsync();
        (await AssociateVat(entry.Id, declaration.Id)).IsFailure.ShouldBeTrue();
        entry.TransactionType.ShouldBe(LedgerTransactionType.Tax);
    }

    [Fact]
    public async Task D301_RejectedOrSupersededDeclarationsAndExistingDocumentsAreRejected()
    {
        DeclarationVersion declaration = D301();
        Transaction(-3304, "ANAF", "D301", iban: "RO49TREZ1234567891234567");
        await Import();
        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        declaration.Status = DeclarationStatus.Rejected;
        await _db.SaveChangesAsync();
        (await AssociateVat(entry.Id, declaration.Id)).IsFailure.ShouldBeTrue();
        declaration.Status = DeclarationStatus.Accepted;
        entry.SourceDocumentId = Guid.NewGuid();
        await _db.SaveChangesAsync();
        (await AssociateVat(entry.Id, declaration.Id)).IsFailure.ShouldBeTrue();
        entry.SourceDocumentId = null;
        _db.DeclarationVersions.Add(new DeclarationVersion { Id = Guid.NewGuid(), DeclarationId = declaration.DeclarationId,
            Declaration = declaration.Declaration, VersionNo = 2, Amount = 3304 });
        await _db.SaveChangesAsync();
        (await AssociateVat(entry.Id, declaration.Id)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task D301_VatPayerOrOtherAccountantsPortfolioCannotBeAssociated()
    {
        DeclarationVersion declaration = D301();
        Transaction(-3304, "ANAF", "D301", iban: "RO49TREZ1234567891234567");
        await Import();
        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        var profile = new Domain.PfaRegistrations.PfaFiscalProfile { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, IsVatPayer = true };
        _db.PfaFiscalProfiles.Add(profile);
        await _db.SaveChangesAsync();
        (await AssociateVat(entry.Id, declaration.Id)).IsFailure.ShouldBeTrue();
        profile.IsVatPayer = false;
        (await _db.PfaRegistrations.SingleAsync(p => p.Id == _pfa)).AssignedContabilId = Guid.NewGuid();
        await _db.SaveChangesAsync();
        (await AssociateVat(entry.Id, declaration.Id)).IsFailure.ShouldBeTrue();
        entry.TransactionType.ShouldBe(LedgerTransactionType.Tax);
    }

    [Fact]
    public async Task Uber_TotalIncomeIncludesOtherServices_AndProducesGrossIncomeAndDeductibleCommission()
    {
        Report(Platform.Uber, 42582, 9973.50m);
        CommissionInvoice(Platform.Uber);
        Transaction(32608.50m, "UBER BV", "Decont august", new DateOnly(2026, 9, 2));
        await Import();
        Application.Accounting.Contracts.RefView fiscal = (await new GetRefQueryHandler(_db).Handle(new GetRefQuery(_pfa, 2026), CancellationToken.None)).Value;
        fiscal.Rows[0].Value.ShouldBe(42582);
        fiscal.Rows[1].Value.ShouldBe(9973.50m);
        fiscal.Rows[2].Value.ShouldBe(32608.50m);
        await Import();
        (await _db.LedgerEntries.CountAsync()).ShouldBe(2);
    }
}
