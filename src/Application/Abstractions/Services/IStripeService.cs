namespace Application.Abstractions.Services;

/// <summary>
/// Abstracts Stripe operations for testability.
/// </summary>
public interface IStripeService
{
    /// <summary>
    /// Creates a Stripe Checkout Session for a one-time payment or subscription.
    /// Returns the session URL to redirect the user.
    /// </summary>
#pragma warning disable CA1054 // URIs should not be hardcoded
    Task<string> CreateCheckoutSessionAsync(
        string priceId,
        string mode,           // "payment" or "subscription"
        string successUrl,
        string cancelUrl,
        string? customerEmail,
        string? userId,
        string? metadata,
        CancellationToken cancellationToken = default);
#pragma warning restore CA1054

    /// <summary>
    /// Creates or retrieves a Stripe Customer for the user.
    /// </summary>
    Task<string> GetOrCreateCustomerAsync(
        string userId,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Constructs a validated Stripe webhook event from the raw payload.
    /// Returns null if signature is invalid.
    /// </summary>
    Stripe.Event? ConstructWebhookEvent(string payload, string stripeSignatureHeader);
}
