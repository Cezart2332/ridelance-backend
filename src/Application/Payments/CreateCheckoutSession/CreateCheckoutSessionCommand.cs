using System.Diagnostics.CodeAnalysis;
using Application.Abstractions.Messaging;
using Domain.Payments;

namespace Application.Payments.CreateCheckoutSession;

/// <summary>
/// Creates a Stripe Checkout Session URL. Returns the redirect URL.
/// </summary>
/// <param name="Cycle">
/// Ciclul de facturare al abonamentului: lunar (implicit) sau anual. Ignorat pe modul "payment",
/// unde nu există reînnoire. A luat locul ancorei de facturare — plata se face acum, la checkout,
/// nu la următoarea zi de luni.
/// </param>
/// <param name="BcrDiscountRequested">
/// Clientul a bifat că își deschide cont BCR. Nu schimbă suma încasată acum: reducerea se aplică
/// abia după ce BCR confirmă contul, deci aici se înregistrează doar intenția.
/// </param>
public sealed record CreateCheckoutSessionCommand(
    Guid UserId,
    string UserEmail,
    string Mode,          // "payment" or "subscription"
    string Plan,          // e.g. "solo", "start", "pro", "infiintare_pfa"
    SubscriptionBillingCycle Cycle = SubscriptionBillingCycle.Monthly,
    [property: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "Strings are preferred for API DTOs")]
    [param: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "Strings are preferred for API DTOs")]
    string? SuccessUrl = null,
    [property: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "Strings are preferred for API DTOs")]
    [param: SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "Strings are preferred for API DTOs")]
    string? CancelUrl = null,
    bool IsPlanChange = false,
    bool BcrDiscountRequested = false
) : ICommand<string>;
