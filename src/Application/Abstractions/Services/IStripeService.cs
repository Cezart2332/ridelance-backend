using Domain.Payments;

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
        /// <summary>
        /// Cheie de idempotență trimisă la Stripe: două cereri cu aceeași cheie întorc aceeași
        /// sesiune, deci un dublu-click nu produce două plăți.
        /// </summary>
        string? idempotencyKey = null,
        /// <summary>
        /// Cupon aplicat pe sesiune. Azi doar creditul avansului plătit în onboarding, care se
        /// întoarce ca reducere pe primele facturi ale abonamentului.
        /// </summary>
        string? couponId = null,
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
    /// Returns the price ID for a catalog item in whichever Stripe account is configured,
    /// looking it up by lookup key and creating it if the account does not have it yet.
    /// </summary>
    Task<string> ResolvePriceIdAsync(
        StripeCatalogItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a discount code (a Stripe coupon plus the promotion code customers type).
    /// </summary>
    Task<DiscountCode> CreateDiscountCodeAsync(
        NewDiscountCode code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aplică reducerea BCR pe un abonament existent: 50 lei pe lună, șase luni.
    /// </summary>
    /// <remarks>
    /// Cuponul e unul singur pentru toți clienții, cu id fix, creat la prima folosire. Se atașează
    /// abonamentului, deci Stripe scade suma de pe facturile următoare și se oprește singur după
    /// cele șase luni — nu avem noi un ceas de urmărit.
    /// </remarks>
    Task ApplyBcrDiscountAsync(
        string stripeSubscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cuponul cu care avansul din onboarding se întoarce la primul abonament, creat la prima
    /// folosire și regăsit după aceea. Întoarce id-ul, de atașat la sesiunea de checkout.
    /// </summary>
    /// <remarks>
    /// Câte un cupon per plan, fiindcă forma reducerii diferă (vezi
    /// <c>Pricing.OnboardingAdvanceCredit</c>). Id-urile sunt fixe, deci al doilea client îl
    /// regăsește pe primul în loc să umple contul cu duplicate identice.
    /// </remarks>
    Task<string> EnsureAdvanceCreditCouponAsync(
        Pricing.OnboardingAdvanceCredit.Spec spec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the discount codes of the configured account, newest first.
    /// </summary>
    Task<IReadOnlyList<DiscountCode>> ListDiscountCodesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables a discount code. Stripe codes cannot be deleted, only deactivated.
    /// </summary>
    Task<DiscountCode> SetDiscountCodeActiveAsync(
        string promotionCodeId,
        bool active,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Constructs a validated Stripe webhook event from the raw payload.
    /// Returns null if signature is invalid.
    /// </summary>
    Stripe.Event? ConstructWebhookEvent(string payload, string stripeSignatureHeader);
}
