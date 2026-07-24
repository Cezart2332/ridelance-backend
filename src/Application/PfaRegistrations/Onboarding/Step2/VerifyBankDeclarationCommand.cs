using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding.Step2;

/// <summary>Adminul validează/respinge manual declarația de cont bancar.</summary>
public sealed record VerifyBankDeclarationCommand(
    Guid RegistrationId,
    BankDeclarationStatus Status,
    string? AdminNote) : ICommand;
