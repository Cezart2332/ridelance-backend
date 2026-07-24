using Application.Abstractions.Messaging;

namespace Application.PfaRegistrations.Onboarding.Step2;

/// <summary>Pasul 2.4 — clientul acceptă consimțămintele pentru contul Oblio.</summary>
public sealed record AcceptOblioConsentsCommand(
    Guid UserId,
    string? AccountEmail,
    bool AccountCreationConsent,
    bool DataProcessingConsent,
    bool EInvoiceConsent,
    bool AutoInvoicingConsent,
    bool RidelanceManagementConsent,
    bool TermsAcceptedConsent) : ICommand<Step2OblioDto>;
