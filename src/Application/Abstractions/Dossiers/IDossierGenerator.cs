namespace Application.Abstractions.Dossiers;

/// <summary>Datele necesare pentru dosarul de autorizație ARR (Pasul 3).</summary>
public sealed record ArrDossierData(
    string ApplicantName,
    string? Cui,
    string? LegalName,
    string? Address,
    string? AgencyName,
    long FeeBani,
    IReadOnlyList<string> IncludedDocuments,
    DateTime GeneratedAtUtc);

/// <summary>O linie de ecusoane în dosarul copie conformă (Pasul 5).</summary>
public sealed record VehicleBadgeLine(string Provider, int SetCount, long TotalBani);

/// <summary>Datele necesare pentru dosarul de copie conformă & ecusoane (Pasul 5).</summary>
public sealed record VehicleDossierData(
    string ApplicantName,
    string? Cui,
    string? LegalName,
    string? PlateNumber,
    string? Vin,
    string? VehicleDescription,
    int CopyYears,
    long CopyFeePerYearBani,
    long CopyTotalFeeBani,
    IReadOnlyList<VehicleBadgeLine> Badges,
    long BadgesTotalBani,
    IReadOnlyList<string> IncludedDocuments,
    DateTime GeneratedAtUtc);

/// <summary>
/// Generează dosarele PDF de onboarding pe backend. Implementarea folosește QuestPDF pentru layout.
/// Formularele oficiale ARR se completează separat din template-uri Acro,
/// aici producem coperta + sumarul + checklistul dosarului.
/// </summary>
public interface IDossierGenerator
{
    /// <summary>Produce PDF-ul dosarului ARR și întoarce conținutul brut.</summary>
    byte[] GenerateArrDossier(ArrDossierData data);

    /// <summary>Produce PDF-ul dosarului copie conformă & ecusoane și întoarce conținutul brut.</summary>
    byte[] GenerateVehicleDossier(VehicleDossierData data);
}
