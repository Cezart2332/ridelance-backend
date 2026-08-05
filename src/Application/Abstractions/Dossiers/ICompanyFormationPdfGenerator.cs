namespace Application.Abstractions.Dossiers;

/// <summary>O pereche etichetă/valoare din fișa solicitantului.</summary>
public sealed record CompanyFormationField(string Label, string Value);

/// <summary>O persoană din dosar (solicitantul sau un proprietar), gata de tipărit.</summary>
public sealed record CompanyFormationPerson(string Title, IReadOnlyList<CompanyFormationField> Fields);

/// <summary>O declarație acceptată, cu textul exact afișat atunci.</summary>
public sealed record CompanyFormationConsentLine(
    string Title,
    string Body,
    string CheckboxLabel,
    string Version,
    DateTime AcceptedAtUtc);

public sealed record CompanyFormationSheetData(
    string ApplicantName,
    IReadOnlyList<CompanyFormationPerson> People,
    IReadOnlyList<CompanyFormationField> Office,
    DateTime GeneratedAtUtc);

public sealed record CompanyFormationConsentProofData(
    string ApplicantName,
    IReadOnlyList<CompanyFormationConsentLine> Consents,
    string? IpAddress,
    string? UserAgent,
    string? DeviceType,
    string? Os,
    string? Browser,
    DateTime SignedAtUtc,
    string PayloadHash,
    DateTime GeneratedAtUtc);

/// <summary>
/// Cele două PDF-uri din pachetul trimis la Consulto: fișa cu datele solicitantului și dovada
/// de consimțământ. Trăiesc lângă generatorul de dosare ARR, pe același QuestPDF.
/// </summary>
public interface ICompanyFormationPdfGenerator
{
    byte[] GenerateApplicantSheet(CompanyFormationSheetData data);

    byte[] GenerateConsentProof(CompanyFormationConsentProofData data);
}
