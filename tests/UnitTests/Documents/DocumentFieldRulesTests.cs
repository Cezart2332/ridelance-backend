using Application.Documents.AiVerification;
using Domain.Documents;
using Shouldly;
using Xunit;

namespace UnitTests.Documents;

/// <summary>Regulile pe câmpuri: data ITP de pe talon și data obținerii categoriei B.</summary>
public sealed class DocumentFieldRulesTests
{
    private static readonly DateOnly FirstRegistration = new(2015, 3, 10);
    private static readonly DateOnly Registration = new(2021, 6, 1);

    /// <summary>Bugul: se citea data înmatriculării de pe mijlocul talonului, nu viza ITP din dreapta.</summary>
    [Fact]
    public void The_latest_itp_stamp_wins() =>
        DocumentFieldRules.LatestItpDate("2024-05-12, 2026-05-12, 2025-05-12", FirstRegistration, Registration)
            .ShouldBe(new DateOnly(2026, 5, 12));

    [Fact]
    public void Registration_dates_are_never_the_itp() =>
        DocumentFieldRules.LatestItpDate("2021-06-01; 2015-03-10", FirstRegistration, Registration).ShouldBeNull();

    [Fact]
    public void Romanian_date_format_is_read() =>
        DocumentFieldRules.LatestItpDate("12.05.2026", null, null).ShouldBe(new DateOnly(2026, 5, 12));

    [Fact]
    public void An_empty_itp_box_gives_no_date()
    {
        DocumentFieldRules.LatestItpDate(null, FirstRegistration, Registration).ShouldBeNull();
        DocumentFieldRules.LatestItpDate("nu se vede", FirstRegistration, Registration).ShouldBeNull();
    }

    [Fact]
    public void The_talon_takes_its_expiry_from_the_itp_box() =>
        DocumentAiCatalog.For(DocumentCategory.Talon)!.ExpiryFromField.ShouldBe(DocumentAiCatalog.TalonItpField);

    [Fact]
    public void Category_b_must_be_read() =>
        DocumentFieldRules.CategoryBProblem(null, new DateOnly(2020, 1, 1)).ShouldNotBeNull();

    /// <summary>Data din 4a e mai nouă decât B — dacă iese invers, s-a citit altă coloană.</summary>
    [Fact]
    public void Category_b_after_issue_date_is_a_misread() =>
        DocumentFieldRules.CategoryBProblem(new DateOnly(2023, 1, 1), new DateOnly(2020, 1, 1)).ShouldNotBeNull();

    [Fact]
    public void A_plausible_category_b_date_is_fine() =>
        DocumentFieldRules.CategoryBProblem(new DateOnly(2010, 1, 1), new DateOnly(2020, 1, 1)).ShouldBeNull();

    [Fact]
    public void Category_b_is_required_on_the_licence() =>
        DocumentAiCatalog.FieldSpec(DocumentCategory.PermisConducere, "category_b_obtained_on")!.Required.ShouldBeTrue();

    [Fact]
    public void Unfit_conclusion_is_spotted()
    {
        DocumentFieldRules.SaysUnfit("INAPT").ShouldBeTrue();
        DocumentFieldRules.SaysUnfit("Apt").ShouldBeFalse();
        DocumentFieldRules.SaysUnfit(null).ShouldBeFalse();
    }

    /// <summary>Un permis de un an și unsprezece luni nu e eligibil.</summary>
    [Fact]
    public void A_licence_under_two_years_is_ineligible() =>
        Domain.PfaRegistrations.EligibilityRules.Evaluate(
                new DateOnly(1990, 1, 1),
                new DateOnly(2024, 11, 8),
                new DateOnly(2034, 1, 1),
                true,
                new DateOnly(2030, 1, 1),
                new DateOnly(2026, 10, 7))
            .Status.ShouldBe(Domain.PfaRegistrations.EligibilityStatus.Ineligible);
}
