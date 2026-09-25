using System.Diagnostics.CodeAnalysis;
using Application.Abstractions.Messaging;
using Application.Payments.ServiceOrders;
using Application.PfaRegistrations.Onboarding.CompanyFormation;

namespace Application.Payments.CreatePublicServiceCheckout;

/// <summary>
/// Comanda unui serviciu individual, cu formularul lui, urmată de plată. Merge și fără cont (de pe
/// site), și din dashboard — atunci <paramref name="UserId"/> leagă comanda de cont.
/// </summary>
public sealed record CreatePublicServiceCheckoutCommand(
    string ServiceKey,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    [property: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "API DTO")]
    [param: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "API DTO")]
    string? SuccessUrl = null,
    [property: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "API DTO")]
    [param: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "API DTO")]
    string? CancelUrl = null,
    ServiceDossierPayload? Dossier = null,
    SignatureContext? Context = null,
    Guid? UserId = null
) : ICommand<string>;
