using Application.Abstractions.Messaging;

namespace Application.PfaRegistrations.Onboarding.Step2;

/// <summary>Pasul 2.3 — clientul declară contul bancar (IBAN + bancă + document de confirmare).</summary>
public sealed record SubmitBankDeclarationCommand(
    Guid UserId,
    string? BankName,
    string Iban,
    Guid? ConfirmationDocumentId) : ICommand<Step2BankDto>;
