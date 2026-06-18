namespace Application.Abstractions.Services;

/// <summary>
/// Abstracts Stripe operations for testability.
/// </summary>
public interface IStripeService
{
    /// <summary>
    /// Creates a Stripe Checkout Session for a one-time payment or subscription.
    /// Returns the session client secret for embedded checkout.
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
        IReadOnlyDictionary<string, string>? sessionMetadata = null,
        CancellationToken cancellationToken = default);
#pragma warning restore CA1054

    /// <summary>
    /// Retrieves the status and customer details of an existing Stripe Checkout Session.
    /// </summary>
    Task<(string Status, string? CustomerEmail)> GetSessionStatusAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or retrieves a Stripe Customer for the user.
    /// </summary>
    Task<string> GetOrCreateCustomerAsync(
        string userId,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an active recurring price by lookup key or creates it in the configured Stripe account.
    /// </summary>
    Task<string> GetOrCreateRecurringPriceAsync(
        string lookupKey,
        string productName,
        long unitAmount,
        string currency,
        string interval,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an active one-time price by lookup key or creates it in the configured Stripe account.
    /// </summary>
    Task<string> GetOrCreateOneTimePriceAsync(
        string lookupKey,
        string productName,
        long unitAmount,
        string currency,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Constructs a validated Stripe webhook event from the raw payload.
    /// Returns null if signature is invalid.
    /// </summary>
    Stripe.Event? ConstructWebhookEvent(string payload, string stripeSignatureHeader);
}
