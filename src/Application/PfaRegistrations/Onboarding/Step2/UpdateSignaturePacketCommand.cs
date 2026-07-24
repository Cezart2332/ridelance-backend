using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding.Step2;

/// <summary>Adminul creează/avansează manual pachetul de semnături (fără integrare încă).</summary>
public sealed record UpdateSignaturePacketCommand(
    Guid RegistrationId,
    SignatureProvider Provider,
    SignaturePacketStatus Status,
    string? ProviderReference,
    string? AdminNote) : ICommand;
