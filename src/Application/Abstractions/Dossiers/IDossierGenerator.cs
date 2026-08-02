namespace Application.Abstractions.Dossiers;

/// <summary>
/// Un document încărcat de driver, atașat în dosar după copertă. <paramref name="Content"/> e
/// conținutul deja decriptat, iar <paramref name="ContentType"/> decide cum e încorporat:
/// imaginile devin o pagină nouă, PDF-urile se importă cu paginile lor cu tot.
/// </summary>
public sealed record DossierAttachment(string Label, string ContentType, byte[] Content);

/// <summary>Datele necesare pentru dosarul de autorizație ARR (Pasul 3).</summary>
public sealed record ArrDossierData(
    string ApplicantName,
    string? Cui,
    string? LegalName,
    string? Address,
    string? AgencyName,
    long FeeBani,
    IReadOnlyList<DossierAttachment> IncludedDocuments,
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
    IReadOnlyList<DossierAttachment> IncludedDocuments,
    DateTime GeneratedAtUtc);

/// <summary>
/// Generează dosarele PDF de onboarding pe backend. Implementarea folosește QuestPDF pentru layout
/// și PdfSharp pentru a lipi documentele încărcate după copertă, ca dosarul rezultat să poată fi
/// depus ca atare. Formularele oficiale ARR se completează separat din template-uri Acro.
/// </summary>
public interface IDossierGenerator
{
    /// <summary>Produce PDF-ul dosarului ARR și întoarce conținutul brut.</summary>
    byte[] GenerateArrDossier(ArrDossierData data);

    /// <summary>Produce PDF-ul dosarului copie conformă & ecusoane și întoarce conținutul brut.</summary>
    byte[] GenerateVehicleDossier(VehicleDossierData data);
}
