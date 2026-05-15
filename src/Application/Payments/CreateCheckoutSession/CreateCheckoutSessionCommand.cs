using Application.Abstractions.Messaging;

namespace Application.Payments.CreateCheckoutSession;

/// <summary>
/// Creates a Stripe Checkout Session URL. Returns the redirect URL.
/// </summary>
public sealed record CreateCheckoutSessionCommand(
    Guid UserId,
    string UserEmail,
    string PriceId,
    string Mode,          // "payment" or "subscription"
    string Plan,          // e.g. "solo", "start", "pro", "infiintare_pfa"
    long? BillingAnchorUnix  // Unix timestamp for Monday 15:00 billing anchor (subscriptions only)
) : ICommand<string>;
