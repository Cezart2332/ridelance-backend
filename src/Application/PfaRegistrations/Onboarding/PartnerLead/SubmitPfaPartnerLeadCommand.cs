using Application.Abstractions.Messaging;

namespace Application.PfaRegistrations.Onboarding.PartnerLead;

/// <summary>Clientul „Nu am PFA" trimite cererea către partenerul de înființare (Consulto).</summary>
public sealed record SubmitPfaPartnerLeadCommand(
    Guid UserId,
    string? Phone,
    string? Email,
    string? County,
    string? HousingType,
    bool DataSharingConsent) : ICommand<PfaPartnerLeadResponse>;
