using Application.Abstractions.Ai;

namespace Application.Documents.AiVerification;

/// <param name="IsBlankTemplate">
/// Formular necompletat. Singurul semnal care respinge: un șablon gol nu e un act incomplet de
/// verificat, e lipsa actului — la fel ca o poză ilizibilă.
/// </param>
/// <param name="Reasons">De ce merge documentul la admin. Gol când nu e nimic suspect.</param>
public sealed record AuthenticityVerdict(bool IsBlankTemplate, IReadOnlyList<string> Reasons)
{
    public static readonly AuthenticityVerdict Clean = new(false, []);
}

/// <summary>
/// Dacă un document arată emis de o instituție, nu doar dacă are textul potrivit.
///
/// Combină ce a văzut modelul (antet, ștampilă, semnătură, „pare făcut acasă”) cu ce spune fișierul
/// despre el însuși (PDF exportat din Word, fără scan și fără semnătură electronică). Funcție pură;
/// decizia e a codului, nu a modelului. Cu excepția șablonului gol, nimic de aici nu respinge:
/// documentele suspecte merg la admin.
/// </summary>
public static class DocumentAuthenticityEvaluator
{
    /// <summary>Câte motive de-ale modelului păstrăm, ca lista să rămână citibilă.</summary>
    private const int MaxModelReasons = 2;

    public static AuthenticityVerdict Evaluate(
        DocumentAiExpectation expectation,
        DocumentAuthenticityReport? report,
        PdfForensicsReport? pdf)
    {
        var reasons = new List<string>();
        bool blank = report?.IsBlankTemplate == true;

        if (report is not null)
        {
            AuthenticityMarkers required = expectation.RequiredMarkers;

            if (required.HasFlag(AuthenticityMarkers.Letterhead) && report.Letterhead == false)
            {
                reasons.Add("Nu are antetul unității care l-a emis.");
            }

            bool signedDigitally = pdf?.HasDigitalSignature == true;
            if (required.HasFlag(AuthenticityMarkers.StampOrSignature) &&
                report.Stamp == false && report.Signature == false && !signedDigitally)
            {
                reasons.Add("Nu are ștampilă, parafă sau semnătură.");
            }

            if (required.HasFlag(AuthenticityMarkers.RegistrationNumber) && report.RegistrationNumber == false)
            {
                reasons.Add("Nu are număr de înregistrare.");
            }

            if (report.AppearsSelfMade == true)
            {
                reasons.Add("Pare scris acasă, nu emis de o instituție.");
                reasons.AddRange(report.SuspicionReasons
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Select(r => r.Trim())
                    .Take(MaxModelReasons));
            }
        }

        if (pdf is { FromTextEditor: true, HasImages: false, HasDigitalSignature: false } &&
            !expectation.DigitalPdfExpected)
        {
            reasons.Add($"PDF generat dintr-un editor de text ({pdf.Tool}), fără scan și fără semnătură electronică.");
        }

        return new AuthenticityVerdict(blank, reasons.Distinct(StringComparer.Ordinal).ToList());
    }
}
