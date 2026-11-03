using Application.Abstractions.Ai;
using Application.Documents.AiVerification;
using Domain.Documents;
using Shouldly;
using Xunit;

namespace UnitTests.Documents;

/// <summary>
/// Ce spune fișierul despre el însuși. AI-ul nu mai judecă autenticitatea; rămâne doar regula
/// deterministă a PDF-ului scris în Word, ca motiv pentru admin.
/// </summary>
public sealed class DocumentAuthenticityTests
{
    private static readonly DocumentAiExpectation Medical = DocumentAiCatalog.For(DocumentCategory.AdeverintaMedicala)!;
    private static readonly DocumentAiExpectation IdCard = DocumentAiCatalog.For(DocumentCategory.CarteIdentitate)!;

    private static readonly PdfForensicsReport WordPdf = new("Microsoft® Word for Microsoft 365", "Microsoft® Word for Microsoft 365", false, false);

    [Fact]
    public void A_photo_or_unknown_file_has_no_reasons() =>
        DocumentAuthenticityEvaluator.Reasons(Medical, null).ShouldBeEmpty();

    [Fact]
    public void A_word_export_without_scan_or_signature_goes_to_admin() =>
        DocumentAuthenticityEvaluator.Reasons(Medical, WordPdf)
            .ShouldHaveSingleItem().ShouldContain("editor de text");

    /// <summary>Un scan pus în Word și salvat ca PDF are imaginea înăuntru: nu e „scris în Word”.</summary>
    [Fact]
    public void A_word_pdf_holding_a_scan_is_fine() =>
        DocumentAuthenticityEvaluator.Reasons(Medical, WordPdf with { HasImages = true }).ShouldBeEmpty();

    /// <summary>PDF-ul digital de la clinică, semnat electronic, e în regulă.</summary>
    [Fact]
    public void A_digitally_signed_pdf_is_fine() =>
        DocumentAuthenticityEvaluator.Reasons(Medical, WordPdf with { HasDigitalSignature = true }).ShouldBeEmpty();

    /// <summary>PDF-ul din RO CEI Reader e generat de aplicație prin definiție.</summary>
    [Fact]
    public void Digital_pdf_categories_skip_the_editor_rule() =>
        DocumentAuthenticityEvaluator.Reasons(IdCard, WordPdf).ShouldBeEmpty();
}
