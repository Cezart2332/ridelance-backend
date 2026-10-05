using Application.Accounting;
using Application.Accounting.FiscalRegister;
using Application.Accounting.Ledger;
using Application.Accounting.Months;
using Application.Accounting.Registers;
using Application.Expenses;
using Application.Expenses.Update;
using Domain.Accounting;
using Domain.Documents;
using Domain.Expenses;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

public sealed partial class LedgerTests
{
    private static readonly DateOnly ExpenseDay = new(2026, 8, 10);

    private DeductibleExpense UploadedExpense(decimal amount = 320m, string? buyer = null, string number = "BF100")
    {
        _db.PfaAccountingSettings.Single(s => s.PfaRegistrationId == _pfa).ValueJson = "\"50_PERCENT\"";
        _db.PfaRegistrations.Single(p => p.Id == _pfa).AssignedContabilId = _accountant;
        var document = new Document { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, UserId = _user,
            Category = DocumentCategory.Cheltuiala, OriginalFileName = "bon-combustibil.pdf", ContentType = "application/pdf" };
        var expense = new DeductibleExpense { Id = Guid.NewGuid(), UserId = _user, PfaRegistrationId = _pfa, DocumentId = document.Id,
            ItemName = "Combustibil auto", CatalogCategory = "Auto/Moto", DeductibleLabel = "50%", AmountRon = amount,
            Year = 2026, Month = 8, ExpenseDate = ExpenseDay, SupplierName = "OMV PETROM", Status = ExpenseStatus.Confirmed, CreatedByUserId = _user };
        _db.Documents.Add(document);
        _db.DeductibleExpenses.Add(expense);
        foreach ((string key, string? value) in new[] { ("supplier_cui", "12345674"), ("document_number", number), ("beneficiary_cui", buyer), ("expense_category", "FUEL"), ("currency", "RON") })
        {
            if (value != null)
            {
                _db.ExtractedFields.Add(new ExtractedField { Id = Guid.NewGuid(), DocumentId = document.Id, FieldKey = key, AiNormalizedValue = value });
            }
        }
        _db.SaveChanges();
        return expense;
    }

    private Task<Result<DeductibleExpenseResponse>> ReviewExpense(DeductibleExpense expense, string method = "Cash", Guid? bank = null,
        decimal amount = 320m, decimal personal = 20m, bool approve = false, Guid? actor = null, string? reason = null) =>
        new UpdateDeductibleExpenseCommandHandler(_db, new FixedUser(actor ?? _accountant), new ExpenseAccountingService(_db, new FixedUser(actor ?? _accountant)))
            .Handle(new UpdateDeductibleExpenseCommand(_pfa, expense.Id, "FUEL", "Combustibil", "100% trimis de client", amount, 2026, 8,
                ExpenseDay, "OMV PETROM", null, "Bon fiscal", true, method, ExpenseDay, bank, "FUEL", personal, null, reason, approve), CancellationToken.None);

    [Fact]
    public async Task UploadedFuel_UsesPfaRules_NotClientPercentage_AndSplitsPersonalItems()
    {
        DeductibleExpense expense = UploadedExpense();
        ExpenseSuggestion suggestion = await new ExpenseAccountingService(_db, new FixedUser(_user)).SuggestAsync(expense, CancellationToken.None);
        suggestion.Category.ShouldBe("FUEL");
        suggestion.Categories.Single(c => c.Category == "FUEL").Percent.ShouldBe(50m);
        (await ReviewExpense(expense, approve: true)).IsSuccess.ShouldBeTrue();
        LedgerEntry payment = await _db.LedgerEntries.SingleAsync();
        payment.Amount.ShouldBe(-320m);
        payment.PersonalAmount.ShouldBe(20m);
        payment.DeductibleAmount.ShouldBe(150m);
        expense.DeductibleLabel.ShouldBe("50%");
        (await _db.Documents.SingleAsync(d => d.Id == expense.DocumentId)).Status.ShouldBe(DocumentStatus.Verified);
        (await new GetRjipQueryHandler(_db).Handle(new GetRjipQuery(_pfa, ExpenseDay, ExpenseDay), CancellationToken.None)).Value.Rows.Single().CashOut.ShouldBe(320m);
        (await new GetRefQueryHandler(_db).Handle(new GetRefQuery(_pfa, 2026), CancellationToken.None)).Value.Rows[1].Value.ShouldBe(150m);
    }

    [Fact]
    public async Task UploadedFuel_100PercentSettingIsAppliedAtPaymentDate()
    {
        DeductibleExpense expense = UploadedExpense();
        VehicleDeductibility(new DateOnly(2026, 8, 1), "100_PERCENT");
        await _db.SaveChangesAsync();
        (await ReviewExpense(expense)).Value.DeductibleAmount.ShouldBe(300m);
    }

    [Fact]
    public async Task UploadedReceipt_ReusesOneExistingBankPayment()
    {
        DeductibleExpense expense = UploadedExpense();
        Transaction(-320m, "OMV PETROM", "Plată card", ExpenseDay);
        await Import();
        LedgerEntry bank = await _db.LedgerEntries.SingleAsync();
        ExpenseSuggestion suggestion = await new ExpenseAccountingService(_db, new FixedUser(_accountant)).SuggestAsync(expense, CancellationToken.None);
        suggestion.Payments.ShouldHaveSingleItem().Id.ShouldBe(bank.Id);
        (await ReviewExpense(expense, "Bank", bank.Id)).IsSuccess.ShouldBeTrue();
        (await _db.LedgerEntries.CountAsync()).ShouldBe(1);
        bank.SourceDocumentId.ShouldBe(expense.DocumentId);
        (await ReviewExpense(expense, "Bank", bank.Id, reason: "Verificare repetată")).IsSuccess.ShouldBeTrue();
        (await _db.LedgerEntries.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task UploadedInvoice_Unpaid_DoesNotInventAPaymentOrTaxDeduction()
    {
        DeductibleExpense expense = UploadedExpense();
        (await ReviewExpense(expense, "Unpaid")).IsSuccess.ShouldBeTrue();
        (await _db.LedgerEntries.CountAsync()).ShouldBe(0);
        (await new GetRefQueryHandler(_db).Handle(new GetRefQuery(_pfa, 2026), CancellationToken.None)).Value.Rows[1].Value.ShouldBe(0m);
        var profile = new Domain.FiscalProfiles.PfaTaxProfile { TaxYear = 2026 };
        Application.FiscalEstimates.FinancialSnapshot snapshot = await new Application.FiscalEstimates.FinancialSnapshotProvider(_db)
            .GetAsync(await _db.PfaRegistrations.SingleAsync(p => p.Id == _pfa), profile, new DateOnly(2026, 8, 31), CancellationToken.None);
        snapshot.DeductibleExpensesYtd.ShouldBe(0m);
    }

    [Fact]
    public async Task ProfitEstimate_UsesActualDeductiblePortion_NotReceiptTotal()
    {
        DeductibleExpense expense = UploadedExpense();
        (await ReviewExpense(expense)).IsSuccess.ShouldBeTrue();
        var profile = new Domain.FiscalProfiles.PfaTaxProfile { TaxYear = 2026 };
        Application.FiscalEstimates.FinancialSnapshot snapshot = await new Application.FiscalEstimates.FinancialSnapshotProvider(_db)
            .GetAsync(await _db.PfaRegistrations.SingleAsync(p => p.Id == _pfa), profile, new DateOnly(2026, 8, 31), CancellationToken.None);
        snapshot.DeductibleExpensesYtd.ShouldBe(150m);
    }

    [Fact]
    public async Task AnafInvoiceAlreadyMatchedToBank_ThenUpload_UsesSamePayment()
    {
        DeductibleExpense expense = UploadedExpense();
        Transaction(-320m, "OMV PETROM", "Factura BF100", ExpenseDay);
        var invoice = new EFacturaMessage { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Kind = EFacturaMessageKind.Received,
            InvoiceNumber = "BF100", SupplierCif = "RO12345674", SupplierName = "OMV PETROM", TotalAmount = 320m, IssueDate = ExpenseDay, Currency = "RON" };
        _db.EFacturaMessages.Add(invoice);
        await _db.SaveChangesAsync();
        await Import();
        LedgerEntry bank = await _db.LedgerEntries.SingleAsync();
        bank.EFacturaMessageId.ShouldBe(invoice.Id);
        (await ReviewExpense(expense, "Bank", bank.Id)).IsSuccess.ShouldBeTrue();
        bank.SourceDocumentId.ShouldBe(expense.DocumentId);
        invoice.PaidAmount.ShouldBe(320m);
        (await _db.LedgerEntries.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CashExpense_CanBeCorrectedInPlaceWithReason_ClosedPeriodsBlockCorrections()
    {
        DeductibleExpense expense = UploadedExpense();
        (await ReviewExpense(expense)).IsSuccess.ShouldBeTrue();
        (await ReviewExpense(expense, amount: 340m, reason: "Corectat totalul citit greșit")).IsSuccess.ShouldBeTrue();
        (await _db.LedgerEntries.CountAsync()).ShouldBe(1);
        LedgerEntry entry = await _db.LedgerEntries.SingleAsync();
        entry.Amount.ShouldBe(-340m);
        entry.DeductibleAmount.ShouldBe(160m);
        _db.PfaAccountingPeriods.Add(new PfaAccountingPeriod { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Period = "2026-08", Status = AccountingPeriodStatus.Closed });
        await _db.SaveChangesAsync();
        (await ReviewExpense(expense, amount: 360m, reason: "Corecție")).IsFailure.ShouldBeTrue();
        entry.Amount.ShouldBe(-340m);
        expense.AmountRon.ShouldBe(340m);
    }

    [Fact]
    public async Task BuyerCuiMismatch_RemainsInReview_AndIsExcludedFromREF()
    {
        DeductibleExpense expense = UploadedExpense(buyer: "87654321");
        (await ReviewExpense(expense)).IsSuccess.ShouldBeTrue();
        (await _db.LedgerEntries.SingleAsync()).ReconciliationStatus.ShouldBe(ReconciliationStatus.NeedsReview);
        (await new GetRefQueryHandler(_db).Handle(new GetRefQuery(_pfa, 2026), CancellationToken.None)).Value.Rows[1].Value.ShouldBe(0m);
    }

    [Fact]
    public async Task UploadedInvoice_ThenAnafOriginal_AttachesToTheSameCashPayment()
    {
        DeductibleExpense expense = UploadedExpense();
        (await ReviewExpense(expense)).IsSuccess.ShouldBeTrue();
        var invoice = new EFacturaMessage { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Kind = EFacturaMessageKind.Received,
            InvoiceNumber = "BF100", SupplierCif = "RO12345674", TotalAmount = 320m, IssueDate = ExpenseDay, Currency = "RON" };
        _db.EFacturaMessages.Add(invoice);
        await _db.SaveChangesAsync();
        await Import();
        (await _db.LedgerEntries.CountAsync()).ShouldBe(1);
        (await _db.LedgerEntries.SingleAsync()).EFacturaMessageId.ShouldBe(invoice.Id);
        invoice.PaidAmount.ShouldBe(320m);
        await Import();
        invoice.PaidAmount.ShouldBe(320m);
        (await _db.LedgerEntries.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task ASecondUploadOfTheSameNumberSupplierDateAndTotal_CannotCreateAnotherPayment()
    {
        DeductibleExpense first = UploadedExpense();
        (await ReviewExpense(first)).IsSuccess.ShouldBeTrue();
        DeductibleExpense copy = UploadedExpense();
        (await ReviewExpense(copy)).Error.Code.ShouldBe("Expense.Duplicate");
        (await _db.LedgerEntries.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task PfaCannotApproveItsOwnDocument_AndAnotherAccountantCannotReviewIt()
    {
        DeductibleExpense expense = UploadedExpense();
        (await ReviewExpense(expense, approve: true, actor: _user)).Error.ShouldBe(ExpenseErrors.AccessDenied);
        (await _db.PfaRegistrations.SingleAsync(p => p.Id == _pfa)).AssignedContabilId = Guid.NewGuid();
        await _db.SaveChangesAsync();
        (await ReviewExpense(expense)).Error.ShouldBe(ExpenseErrors.AccessDenied);
    }

    [Theory]
    [InlineData("2026-09", "2026-10-01T00:00:00Z", true)]
    [InlineData("2026-10", "2026-10-05T12:00:00Z", false)]
    [InlineData("2026-11", "2026-10-05T12:00:00Z", false)]
    [InlineData("2026-09", "2026-09-30T21:00:00Z", true)]
    public void MonthlyDeclarations_AreAvailableOnlyForCompletedRomanianCalendarMonths(string period, string utc, bool allowed) =>
        MonthlyDeclarationPeriod.IsCompleted(period, DateTime.Parse(utc, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime()).ShouldBe(allowed);
}
