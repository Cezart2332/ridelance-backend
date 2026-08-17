namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>Adresă, în forma în care circulă spre client.</summary>
public sealed record AdresaDto(
    string? Judet,
    string? Localitate,
    string? Strada,
    string? Numar,
    string? Bloc,
    string? Scara,
    string? Etaj,
    string? Apartament,
    /// <summary>Șase cifre. Obligatoriu pentru sediul social, opțional în rest.</summary>
    string? CodPostal);

/// <summary>
/// Datele de identitate ale unei persoane din dosar. CNP-ul circulă în clar doar către
/// proprietarul dosarului — adminul îl vede mascat, cu dezvăluire la cerere și audit.
/// </summary>
public sealed record PersoanaFizicaDto(
    string? Nume,
    string? Prenume,
    string? Cnp,
    string? CnpMasked,
    string TipAct,
    string? SerieAct,
    string? NumarAct,
    string? AutoritateEmitenta,
    DateOnly? DataEmiterii,
    DateOnly? DataExpirarii,
    AdresaDto Domiciliu);

public sealed record CompanyFormationOwnerDto(Guid Id, int Position, PersoanaFizicaDto Persoana);

public sealed record ConsultoOfficeDto(
    Guid Id,
    string Adresa,
    int MonthlyFeeBani,
    int YearlyFeeBani);

public sealed record CompanyFormationOfficeDto(
    string? Type,
    Guid? ConsultoOfficeId,
    bool? IsOwner,
    AdresaDto Adresa,
    bool AcknowledgedOwnershipDocs,
    bool AcknowledgedSubmitLater,
    bool? AcknowledgedOwnerConsent);

public sealed record CompanyFormationSignatureDto(
    DateTime SignedAtUtc,
    Guid? ImageDocumentId);

/// <summary>Starea completă a dosarului de înființare, pentru reluare de unde a rămas.</summary>
public sealed record CompanyFormationResponse(
    Guid? Id,
    Guid? PfaRegistrationId,
    string Status,
    string CurrentStage,
    bool IsLocked,
    string? AdminNote,
    PersoanaFizicaDto Solicitant,
    /// <remarks>Câmpurile completate de OCR și neatinse de user — poartă indicatorul „din CI".</remarks>
    IReadOnlyList<string> PrefilledFields,
    CompanyFormationOfficeDto Office,
    IReadOnlyList<CompanyFormationOwnerDto> Owners,
    bool PersonalDataComplete,
    bool RegisteredOfficeComplete,
    CompanyFormationSignatureDto? Signature);

// --- Payload-uri de intrare ---

public sealed record AdresaPayload(
    string? Judet,
    string? Localitate,
    string? Strada,
    string? Numar,
    string? Bloc,
    string? Scara,
    string? Etaj,
    string? Apartament,
    string? CodPostal);

public sealed record PersoanaFizicaPayload(
    string? Nume,
    string? Prenume,
    string? Cnp,
    string? TipAct,
    string? SerieAct,
    string? NumarAct,
    string? AutoritateEmitenta,
    DateOnly? DataEmiterii,
    DateOnly? DataExpirarii,
    AdresaPayload? Domiciliu);

/// <summary>
/// Un proprietar al imobilului declarat ca sediu. <paramref name="Id"/> lipsește pentru cei
/// adăugați în pagină și încă nesalvați.
/// </summary>
public sealed record OwnerPayload(Guid? Id, PersoanaFizicaPayload Persoana);

/// <summary>
/// Etapa 2, trimisă întreagă la fiecare autosave: lista de proprietari se reconciliază după
/// id, deci un „Elimină" nu are nevoie de un endpoint separat.
/// </summary>
public sealed record RegisteredOfficePayload(
    string? Type,
    Guid? ConsultoOfficeId,
    bool? IsOwner,
    AdresaPayload? Adresa,
    bool AcknowledgedOwnershipDocs,
    bool AcknowledgedSubmitLater,
    bool? AcknowledgedOwnerConsent,
    IReadOnlyList<OwnerPayload>? Owners);
