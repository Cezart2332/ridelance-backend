using Application.Documents.AiVerification;
using Shouldly;
using Xunit;

namespace UnitTests.Documents;

/// <summary>
/// Verificarea temporală a documentelor. Testele fixează ziua de referință, ca să nu depindă de
/// ceasul mașinii pe care rulează — exact problema care a produs bugul: modelul „știa" o dată
/// curentă și respingea acte eliberate în trecut ca fiind în viitor.
/// </summary>
public sealed class DocumentDateValidatorTests
{
    private static readonly DateOnly Today = new(2026, 8, 5);

    private static DocumentDateVerdict Evaluate(
        DateOnly? issuedOn = null,
        DateOnly? expiresAt = null,
        bool expectsExpiryDate = false,
        bool issueDateOnly = false,
        int? validMonthsFromIssue = null) =>
        DocumentDateValidator.Evaluate(
            issuedOn, expiresAt, expectsExpiryDate, issueDateOnly, Today, validMonthsFromIssue);

    // --- Data eliberării ---

    [Fact]
    public void Issue_date_in_the_past_is_accepted()
    {
        // Cazul din bug: 15.09.2025 la data curentă 2026-08-05.
        Evaluate(issuedOn: new DateOnly(2025, 9, 15)).Outcome.ShouldBe(DocumentDateOutcome.Accepted);
    }

    [Fact]
    public void Issue_date_equal_to_today_is_accepted()
    {
        // Un act poate fi eliberat chiar azi.
        Evaluate(issuedOn: Today).Outcome.ShouldBe(DocumentDateOutcome.Accepted);
    }

    [Fact]
    public void Issue_date_in_the_future_is_rejected()
    {
        DocumentDateVerdict verdict = Evaluate(issuedOn: Today.AddDays(1));

        verdict.Outcome.ShouldBe(DocumentDateOutcome.Rejected);
        verdict.Reason.ShouldContain("viitor");
    }

    [Fact]
    public void Missing_dates_are_accepted_when_the_document_has_none()
    {
        Evaluate().Outcome.ShouldBe(DocumentDateOutcome.Accepted);
    }

    [Fact]
    public void Missing_expiry_needs_review_when_the_document_should_have_one()
    {
        // Nu respingem pe baza a ceva ce nu s-a putut citi — decide un om.
        Evaluate(expectsExpiryDate: true).Outcome.ShouldBe(DocumentDateOutcome.NeedsManualReview);
    }

    [Fact]
    public void Implausibly_old_issue_date_needs_review()
    {
        Evaluate(issuedOn: new DateOnly(1889, 9, 15)).Outcome.ShouldBe(DocumentDateOutcome.NeedsManualReview);
    }

    // --- Expirarea ---

    [Fact]
    public void Expired_document_is_rejected()
    {
        DocumentDateVerdict verdict = Evaluate(expiresAt: Today.AddDays(-1), expectsExpiryDate: true);

        verdict.Outcome.ShouldBe(DocumentDateOutcome.Rejected);
        verdict.Reason.ShouldContain("expirat");
    }

    [Fact]
    public void Document_expiring_today_is_still_valid_today()
    {
        Evaluate(expiresAt: Today, expectsExpiryDate: true).Outcome.ShouldBe(DocumentDateOutcome.Accepted);
    }

    [Fact]
    public void Expiry_far_in_the_future_needs_review()
    {
        // 3025 vine dintr-o citire greșită, nu dintr-un act real.
        Evaluate(expiresAt: new DateOnly(3025, 1, 1), expectsExpiryDate: true)
            .Outcome.ShouldBe(DocumentDateOutcome.NeedsManualReview);
    }

    [Fact]
    public void Inconsistent_dates_on_an_old_document_read_as_expired()
    {
        // Expirare înaintea eliberării, ambele în trecut: motivul util pentru client e că
        // documentul e expirat, nu că datele sunt inconsistente.
        DocumentDateVerdict verdict = Evaluate(
            issuedOn: new DateOnly(2025, 9, 15),
            expiresAt: new DateOnly(2025, 1, 1),
            expectsExpiryDate: true);

        verdict.Outcome.ShouldBe(DocumentDateOutcome.Rejected);
        verdict.Reason.ShouldContain("expirat");
    }

    // --- Certificatul de înregistrare: data e a eliberării, nu a expirării ---

    [Fact]
    public void Registration_certificate_never_expires_however_old()
    {
        // Un PFA înființat în 2015 are un certificat perfect valabil.
        Evaluate(issuedOn: new DateOnly(2015, 3, 20), issueDateOnly: true)
            .Outcome.ShouldBe(DocumentDateOutcome.Accepted);
    }

    [Fact]
    public void Registration_certificate_with_an_old_date_in_the_expiry_slot_is_not_rejected()
    {
        // Chiar dacă modelul pune data eliberării în „expires_at", nu o tratăm ca expirare.
        Evaluate(expiresAt: new DateOnly(2015, 3, 20), issueDateOnly: true)
            .Outcome.ShouldBe(DocumentDateOutcome.Accepted);
    }

    [Fact]
    public void Registration_certificate_issued_in_the_future_is_still_rejected()
    {
        Evaluate(issuedOn: Today.AddDays(30), issueDateOnly: true)
            .Outcome.ShouldBe(DocumentDateOutcome.Rejected);
    }

    // --- Valabilitate derivată din eliberare (cazierul: 6 luni) ---

    [Fact]
    public void Validity_is_derived_from_the_issue_date_when_the_document_omits_it()
    {
        Evaluate(issuedOn: Today.AddMonths(-7), expectsExpiryDate: true, validMonthsFromIssue: 6)
            .Outcome.ShouldBe(DocumentDateOutcome.Rejected);

        Evaluate(issuedOn: Today.AddMonths(-2), expectsExpiryDate: true, validMonthsFromIssue: 6)
            .Outcome.ShouldBe(DocumentDateOutcome.Accepted);
    }

    // --- Parsarea ---

    [Theory]
    [InlineData("2025-09-15")]
    [InlineData("15.09.2025")]
    [InlineData("15/09/2025")]
    [InlineData("15-09-2025")]
    [InlineData("2025/09/15")]
    [InlineData("  2025-09-15  ")]
    public void Parses_the_formats_the_model_may_return(string value)
    {
        DocumentDateValidator.Parse(value).ShouldBe(new DateOnly(2025, 9, 15));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2O25-09-15")]      // OCR: litera O în loc de zero
    [InlineData("15.09.2O25")]
    [InlineData("2025-13-01")]      // lună inexistentă
    [InlineData("31.02.2025")]      // zi inexistentă
    [InlineData("necunoscut")]
    public void Returns_null_for_anything_that_is_not_a_real_date(string? value)
    {
        DocumentDateValidator.Parse(value).ShouldBeNull();
    }

    [Fact]
    public void An_unparseable_date_behaves_like_a_missing_one()
    {
        // „2O25" nu devine niciodată o dată: documentul intră la om, nu e respins.
        DateOnly? parsed = DocumentDateValidator.Parse("2O25-09-15");

        parsed.ShouldBeNull();
        Evaluate(issuedOn: parsed, expectsExpiryDate: true)
            .Outcome.ShouldBe(DocumentDateOutcome.NeedsManualReview);
    }

    [Fact]
    public void Today_in_Romania_is_resolvable_on_this_machine()
    {
        DocumentDateValidator.TodayInRomania().Year.ShouldBeGreaterThan(2000);
    }
}
