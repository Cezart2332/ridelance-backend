using Application.Abstractions.Ai;
using Application.Documents.AiVerification;
using Domain.Documents;
using Shouldly;
using Xunit;

namespace UnitTests.Documents;

/// <summary>
/// Autenticitatea documentelor de înrolare. Bugul: o adeverință medicală scrisă în Word sau un
/// formular gol treceau ca „corecte”, fiindcă verificarea se uita doar dacă textul seamănă.
/// </summary>
public sealed class DocumentAuthenticityTests
{
    private static readonly DocumentAiExpectation Medical = DocumentAiCatalog.For(DocumentCategory.AdeverintaMedicala)!;
    private static readonly DocumentAiExpectation IdCard = DocumentAiCatalog.For(DocumentCategory.CarteIdentitate)!;

    private static DocumentAuthenticityReport Report(
        bool? letterhead = true,
        bool? stamp = true,
        bool? signature = true,
        bool? blank = false,
        bool? selfMade = false,
        params string[] reasons) =>
        new(letterhead, stamp, signature, true, "Clinica X", blank, selfMade, true, reasons);

    private static readonly PdfForensicsReport WordPdf = new("Microsoft® Word for Microsoft 365", "Microsoft® Word for Microsoft 365", false, false);

    [Fact]
    public void A_proper_medical_certificate_is_clean() =>
        DocumentAuthenticityEvaluator.Evaluate(Medical, Report(), null).ShouldBe(AuthenticityVerdict.Clean, new VerdictComparer());

    [Fact]
    public void A_blank_template_is_rejected() =>
        DocumentAuthenticityEvaluator.Evaluate(Medical, Report(blank: true), null).IsBlankTemplate.ShouldBeTrue();

    [Fact]
    public void Missing_stamp_and_signature_goes_to_admin()
    {
        AuthenticityVerdict verdict = DocumentAuthenticityEvaluator.Evaluate(Medical, Report(stamp: false, signature: false), null);

        verdict.IsBlankTemplate.ShouldBeFalse();
        verdict.Reasons.ShouldHaveSingleItem().ShouldContain("ștampilă");
    }

    /// <summary>PDF-ul digital de la clinică, semnat electronic, nu are ștampilă pe hârtie — și e în regulă.</summary>
    [Fact]
    public void A_digital_signature_counts_as_signature()
    {
        var signedPdf = new PdfForensicsReport("Clinic Software", null, false, HasDigitalSignature: true);

        DocumentAuthenticityEvaluator.Evaluate(Medical, Report(stamp: false, signature: false), signedPdf).Reasons.ShouldBeEmpty();
    }

    [Fact]
    public void Missing_letterhead_goes_to_admin() =>
        DocumentAuthenticityEvaluator.Evaluate(Medical, Report(letterhead: false), null).Reasons.ShouldHaveSingleItem();

    [Fact]
    public void Self_made_documents_carry_the_models_reasons()
    {
        AuthenticityVerdict verdict = DocumentAuthenticityEvaluator.Evaluate(
            Medical, Report(selfMade: true, reasons: "Text simplu, fără antet"), null);

        verdict.Reasons.Count.ShouldBe(2);
        verdict.Reasons.ShouldContain("Text simplu, fără antet");
    }

    [Fact]
    public void A_word_export_without_scan_or_signature_goes_to_admin() =>
        DocumentAuthenticityEvaluator.Evaluate(Medical, Report(), WordPdf)
            .Reasons.ShouldHaveSingleItem().ShouldContain("editor de text");

    /// <summary>Un scan pus în Word și salvat ca PDF are imaginea înăuntru: nu e „scris în Word”.</summary>
    [Fact]
    public void A_word_pdf_holding_a_scan_is_fine() =>
        DocumentAuthenticityEvaluator.Evaluate(Medical, Report(), WordPdf with { HasImages = true }).Reasons.ShouldBeEmpty();

    /// <summary>PDF-ul din RO CEI Reader e generat de aplicație prin definiție.</summary>
    [Fact]
    public void Digital_pdf_categories_skip_the_editor_rule() =>
        DocumentAuthenticityEvaluator.Evaluate(IdCard, null, WordPdf).Reasons.ShouldBeEmpty();

    /// <summary>Un răspuns fără blocul de autenticitate nu trimite nimic la admin.</summary>
    [Fact]
    public void Unknown_markers_do_not_flag() =>
        DocumentAuthenticityEvaluator.Evaluate(
                Medical, new DocumentAuthenticityReport(null, null, null, null, null, null, null, null, []), null)
            .ShouldBe(AuthenticityVerdict.Clean, new VerdictComparer());

    private sealed class VerdictComparer : IEqualityComparer<AuthenticityVerdict>
    {
        public bool Equals(AuthenticityVerdict? x, AuthenticityVerdict? y) =>
            x is not null && y is not null && x.IsBlankTemplate == y.IsBlankTemplate && x.Reasons.SequenceEqual(y.Reasons);

        public int GetHashCode(AuthenticityVerdict obj) => obj.IsBlankTemplate.GetHashCode();
    }
}
