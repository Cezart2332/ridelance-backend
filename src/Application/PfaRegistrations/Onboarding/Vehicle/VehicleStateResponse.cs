namespace Application.PfaRegistrations.Onboarding.Vehicle;

public sealed record VehicleBadgeDto(
    string Provider,
    int SetCount,
    long FeePerSetSnapshotBani,
    long TotalFeeSnapshotBani,
    string Status,
    Guid? BadgeDocumentId);

public sealed record CopyRequestDto(
    int Years,
    long FeePerYearSnapshotBani,
    long TotalFeeSnapshotBani,
    string Status,
    bool HasDossier,
    Guid? DossierDocumentId,
    DateTime? DossierGeneratedAtUtc,
    DateTime? SubmittedAtUtc,
    Guid? CopyConformaDocumentId,
    string? CopyConformaNumber,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    string? AdminNote);

/// <summary>Starea completă a Pasului 5 (vehicul + copie conformă + ecusoane).</summary>
public sealed record VehicleStateResponse(
    Guid? VehicleId,
    string OwnershipMode,
    bool AddLater,
    string? PlateNumber,
    string? Vin,
    string? Make,
    string? Model,
    int? FirstRegistrationYear,
    Guid? MarketplaceCarId,
    string Status,
    CopyRequestDto? CopyRequest,
    IReadOnlyList<VehicleBadgeDto> Badges,
    long CopyFeePerYearBani,
    long BadgeFeePerSetBani,
    int MaxCopyYears);
