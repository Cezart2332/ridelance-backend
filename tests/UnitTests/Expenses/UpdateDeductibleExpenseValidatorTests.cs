using Application.Expenses.Update;
using FluentValidation.Results;
using Shouldly;
using Xunit;

namespace UnitTests.Expenses;

/// <summary>
/// Confirmarea unei cheltuieli are efect imediat asupra profitului real estimat, așa că
/// regulile de aici sunt cele care decid ce ajunge într-un calcul pe care utilizatorul îl
/// folosește ca să-și pună bani deoparte.
/// </summary>
public sealed class UpdateDeductibleExpenseValidatorTests
{
    private static readonly UpdateDeductibleExpenseCommandValidator Validator = new();

    private static UpdateDeductibleExpenseCommand Command(
        decimal? amount = 284m,
        decimal? vat = 45.36m,
        bool confirm = true) =>
        new(
            PfaRegistrationId: Guid.NewGuid(),
            ExpenseId: Guid.NewGuid(),
            CatalogCategory: "Combustibil",
            ItemName: "Motorină",
            DeductibleLabel: "100%",
            AmountRon: amount,
            Year: 2026,
            Month: 8,
            ExpenseDate: new DateOnly(2026, 8, 10),
            SupplierName: "MOL",
            VatAmount: vat,
            DocumentTypeLabel: "Bon fiscal",
            Confirm: confirm);

    private static ValidationResult Validate(UpdateDeductibleExpenseCommand command) =>
        Validator.Validate(command);

    [Fact]
    public void A_complete_expense_is_accepted()
    {
        Validate(Command()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Confirming_without_an_amount_is_refused()
    {
        // Fără sumă n-ar avea ce intra în profit — dar ar apărea ca înregistrată.
        ValidationResult result = Validate(Command(amount: null, vat: null, confirm: true));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("sumă", StringComparison.Ordinal));
    }

    [Fact]
    public void A_draft_may_stay_without_an_amount()
    {
        // Exact starea de după un OCR care n-a putut citi totalul: se salvează, nu se calculează.
        Validate(Command(amount: null, vat: null, confirm: false)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Vat_above_the_total_is_refused()
    {
        ValidationResult result = Validate(Command(amount: 100m, vat: 120m));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("TVA", StringComparison.Ordinal));
    }

    [Fact]
    public void Vat_equal_to_the_total_is_still_plausible()
    {
        Validate(Command(amount: 100m, vat: 100m)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_missing_vat_is_fine()
    {
        // Multe bonuri nu separă TVA-ul; lipsa lui nu invalidează cheltuiala.
        Validate(Command(vat: null)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Negative_amounts_are_refused()
    {
        Validate(Command(amount: -10m, vat: null)).IsValid.ShouldBeFalse();
    }
}
