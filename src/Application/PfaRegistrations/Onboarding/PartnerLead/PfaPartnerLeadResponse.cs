namespace Application.PfaRegistrations.Onboarding.PartnerLead;

public sealed record PfaPartnerLeadResponse(
    Guid Id,
    Guid PfaRegistrationId,
    string Provider,
    string? Phone,
    string? Email,
    string? County,
    string? HousingType,
    bool DataSharingConsent,
    string Status,
    string? AdminNote);
