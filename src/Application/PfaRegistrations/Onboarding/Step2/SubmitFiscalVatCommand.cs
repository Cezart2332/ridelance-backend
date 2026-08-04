using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;

namespace Application.PfaRegistrations.Onboarding.Step2;

/// <summary>
/// Pasul 2.1 — clientul declară dacă are cod de TVA intracomunitar (art. 317).
/// Tipul de înregistrare nu mai vine de la client: întrebarea e despre un singur regim,
/// așa că îl derivăm din răspuns și nu putem primi o combinație contradictorie.
/// </summary>
public sealed record SubmitFiscalVatCommand(Guid UserId, VatAnswer VatAnswer) : ICommand;
