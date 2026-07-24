using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding.PartnerLead;

/// <summary>Adminul avansează manual statusul lead-ului către partener (fără API extern încă).</summary>
public sealed record UpdatePfaPartnerLeadStatusCommand(
    Guid RegistrationId,
    PfaPartnerLeadStatus Status,
    string? AdminNote) : ICommand<PfaPartnerLeadResponse>;
