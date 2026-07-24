namespace Application.PfaRegistrations.Onboarding.Arr;

public sealed record ArrStateResponse(
    Guid? PfaRegistrationId,
    string? AgencyName,
    long FeeSnapshotBani,
    string SubmissionMethod,
    string Status,
    bool HasDossier,
    Guid? DossierDocumentId,
    DateTime? DossierGeneratedAtUtc,
    DateTime? SubmittedAtUtc,
    Guid? AuthorizationDocumentId,
    string? AuthorizationNumber,
    DateOnly? AuthorizationIssuedOn,
    DateOnly? AuthorizationExpiresOn,
    string? AdminNote);
