using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding.Step2;

/// <summary>Pasul 2.1 — clientul răspunde la întrebarea despre TVA.</summary>
public sealed record SubmitFiscalVatCommand(
    Guid UserId,
    VatAnswer VatAnswer,
    VatRegistrationKind VatRegistrationKind) : ICommand;
