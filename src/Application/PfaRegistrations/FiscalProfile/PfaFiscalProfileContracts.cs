using Application.Abstractions.Messaging;

namespace Application.PfaRegistrations.FiscalProfile;

public sealed record GetPfaFiscalProfileQuery(Guid PfaRegistrationId)
    : IQuery<PfaFiscalSettingsResponse>;

public sealed record UpsertPfaFiscalProfileCommand(
    Guid PfaRegistrationId,
    string SpecialVatCodeStatus,
    DateTime? SpecialVatCodeObtainedAtUtc,
    Guid? SpecialVatCodeDocumentId,
    string UberStatus,
    string BoltStatus,
    string OtherPlatformsStatus,
    string CashRevenueStatus,
    string CashRegisterStatus,
    string VehicleUsageType,
    string? VehicleSupportingDocumentLabel,
    Guid? VehicleSupportingDocumentId) : ICommand<PfaFiscalProfileResponse>;

public sealed record UpsertPfaPlatformAccountsCommand(
    Guid PfaRegistrationId,
    IReadOnlyList<UpsertPfaPlatformAccountItem> Accounts) : ICommand<IReadOnlyList<PfaPlatformAccountResponse>>;

public sealed record MarkPfaFleetAccountConfiguredCommand(
    Guid PfaRegistrationId,
    string Provider) : ICommand<PfaPlatformAccountResponse>;

public sealed record AcceptPfaFleetConsentCommand(
    Guid PfaRegistrationId,
    bool FleetAccountsAccepted,
    bool BoltApiAccepted) : ICommand<PfaFleetConsentResponse>;

public sealed record UpsertPfaPlatformAccountItem(
    string Provider,
    string Kind,
    string? Email,
    string? Phone,
    string? FullName,
    string? Status);

public sealed record PfaFiscalSettingsResponse(
    PfaFiscalProfileResponse FiscalProfile,
    IReadOnlyList<PfaPlatformAccountResponse> PlatformAccounts,
    PfaFleetConsentResponse FleetConsent);

public sealed record PfaFiscalProfileResponse(
    Guid? Id,
    Guid PfaRegistrationId,
    string TaxationSystem,
    bool IsVatPayer,
    bool HasEmployees,
    string AccountingRegime,
    string SpecialVatCodeStatus,
    DateTime? SpecialVatCodeObtainedAtUtc,
    Guid? SpecialVatCodeDocumentId,
    string UberStatus,
    string BoltStatus,
    string OtherPlatformsStatus,
    string CashRevenueStatus,
    string CashRegisterStatus,
    string VehicleUsageType,
    string? VehicleSupportingDocumentLabel,
    Guid? VehicleSupportingDocumentId,
    DateTime? UpdatedAtUtc);

public sealed record PfaPlatformAccountResponse(
    Guid? Id,
    Guid PfaRegistrationId,
    string Provider,
    string Kind,
    string? Email,
    string? Phone,
    string? FullName,
    string Status,
    DateTime? ConfiguredAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record PfaFleetConsentResponse(
    Guid? Id,
    Guid PfaRegistrationId,
    bool FleetAccountsAccepted,
    DateTime? FleetAccountsAcceptedAtUtc,
    bool BoltApiAccepted,
    DateTime? BoltApiAcceptedAtUtc,
    string ConsentTextVersion);
