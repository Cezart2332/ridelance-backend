using SharedKernel;

namespace Application.Abstractions.Ai;

/// <summary>Un câmp de business cerut modelului spre extragere (precompletare).</summary>
public sealed record AiFieldRequest(string Key, string Description, string Type, bool Required);

public sealed record DocumentAiAnalysisRequest(
    byte[] FileBytes,
    string ContentType,
    string FileName,
    string ExpectedDocumentLabel,
    string ExpectationDetails,
    bool ExpectsExpiryDate,
    IReadOnlyList<AiFieldRequest> Fields,
    string? AuthenticityHints = null);

/// <summary>
/// Ce a văzut modelul pe document în afară de text: elementele care fac un act oficial. Fiecare
/// valoare e <c>null</c> când modelul n-a răspuns la ea — necunoscut, nu „lipsește”.
/// </summary>
/// <param name="IsBlankTemplate">Formular sau șablon necompletat: rubricile de date sunt goale.</param>
/// <param name="AppearsSelfMade">
/// Arată scris acasă — într-un editor de text, de mână pe o foaie albă, decupat dintr-un alt act —
/// nu emis de o instituție.
/// </param>
/// <param name="SuspicionReasons">Ce anume i s-a părut în neregulă, în română, pe scurt.</param>
public sealed record DocumentAuthenticityReport(
    bool? Letterhead,
    bool? Stamp,
    bool? Signature,
    bool? RegistrationNumber,
    string? IssuerName,
    bool? IsBlankTemplate,
    bool? AppearsSelfMade,
    bool? IsScanOrPhotoOfPaper,
    IReadOnlyList<string> SuspicionReasons);

/// <summary>Valoarea extrasă pentru un câmp + încrederea auto-raportată de model (0..1).</summary>
public sealed record AiFieldResult(string Key, string? Value, double Confidence);

/// <summary>
/// Ce a citit modelul din document. <b>Doar extragere</b>: modelul nu decide dacă documentul e
/// valid și nu compară date cu prezentul.
///
/// Motivul e concret: modelul nu are un ceas. Îi injectam data curentă în prompt și îl puneam să
/// judece expirarea, iar el respingea acte perfect valabile pentru că „data eliberării e în
/// viitor". Comparațiile temporale se fac acum în C#, pe ceasul serverului
/// (vezi <c>DocumentDateValidator</c>).
/// </summary>
/// <param name="IssuedOn">
/// Data eliberării/emiterii, ISO 8601. Null când documentul nu o conține sau nu s-a putut citi.
/// </param>
/// <param name="ExpiresAt">
/// Data expirării/valabilității, ISO 8601. Null când documentul nu o conține.
/// </param>
/// <param name="RotationDegrees">
/// Cu câte grade trebuie rotită imaginea în sensul acelor de ceasornic ca actul să apară drept:
/// 0, 90, 180 sau 270.
///
/// Se cere modelului fiindcă nu se poate deduce din pixeli. Pe o diplomă reală, scorurile
/// statistice ale celor patru rotații au ieșit −0.043 / −0.053 / −0.060 / −0.060 — zgomot.
/// Distribuția cernelii spune dacă rândurile sunt orizontale, dar nu spune unde e susul, iar
/// modelul care oricum citește documentul știe asta din prima.
/// </param>
public sealed record DocumentAiAnalysisResult(
    bool MatchesExpectedType,
    bool IsReadable,
    DateOnly? IssuedOn,
    DateOnly? ExpiresAt,
    string DetectedType,
    string Reason,
    IReadOnlyList<AiFieldResult> Fields,
    double OverallConfidence,
    int RotationDegrees = 0,
    DocumentAuthenticityReport? Authenticity = null);

public interface IDocumentAiAnalyzer
{
    Task<Result<DocumentAiAnalysisResult>> AnalyzeAsync(
        DocumentAiAnalysisRequest request,
        CancellationToken cancellationToken);
}
