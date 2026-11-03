using Application.Abstractions.Ai;

namespace Application.Documents.AiVerification;

/// <summary>
/// Ce spune fișierul despre el însuși: un PDF exportat din Word, fără scan și fără semnătură
/// electronică, nu e un act emis de o instituție. Funcție pură, fără model: AI-ul doar citește
/// documentul, nu judecă dacă e autentic. Nimic de aici nu respinge — motivul ajunge la admin.
/// </summary>
public static class DocumentAuthenticityEvaluator
{
    public static IReadOnlyList<string> Reasons(DocumentAiExpectation expectation, PdfForensicsReport? pdf)
    {
        if (pdf is { FromTextEditor: true, HasImages: false, HasDigitalSignature: false } &&
            !expectation.DigitalPdfExpected)
        {
            return [$"PDF generat dintr-un editor de text ({pdf.Tool}), fără scan și fără semnătură electronică."];
        }

        return [];
    }
}
