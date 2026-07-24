using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding.Step2;

/// <summary>Adminul avansează manual statusul integrării Oblio (fără API extern încă).</summary>
public sealed record AdvanceOblioIntegrationCommand(
    Guid RegistrationId,
    OblioIntegrationStatus IntegrationStatus,
    string? AdminNote) : ICommand;
