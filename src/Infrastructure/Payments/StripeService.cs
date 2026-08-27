using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Services;
using Domain.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;

namespace Infrastructure.Payments;

/// <summary>
/// Stripe service implementation using Stripe.net SDK.
/// </summary>
internal sealed class StripeService : IStripeService
{
    /// <summary>
    /// Resolved price IDs, shared across requests because the service itself is scoped.
    /// Keyed by API-key fingerprint so a key swap at runtime cannot serve IDs from the old account.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> PriceIdCache = new(StringComparer.Ordinal);

    private readonly ILogger<StripeService> _logger;
    private readonly string _webhookSecret;
    private readonly string _apiKeyFingerprint;

    public StripeService(IConfiguration configuration, IHostEnvironment environment, ILogger<StripeService> logger)
    {
        _logger = logger;

        string apiKey = configuration["Stripe:SecretKey"]
            ?? Environment.GetEnvironmentVariable("Stripe__SecretKey")
            ?? throw new InvalidOperationException("Stripe SecretKey is not configured.");

        _webhookSecret = configuration["Stripe:WebhookSecret"]
            ?? Environment.GetEnvironmentVariable("Stripe__WebhookSecret")
            ?? string.Empty;

        // The webhook endpoint is anonymous, so without signature verification anyone could POST a
        // forged checkout.session.completed and grant themselves a subscription. Only dev may skip it.
        if (string.IsNullOrEmpty(_webhookSecret) && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Stripe WebhookSecret is not configured. It is required outside Development, " +
                "otherwise webhook signatures cannot be verified.");
        }

        _apiKeyFingerprint = Fingerprint(apiKey);

        StripeConfiguration.ApiKey = apiKey;
    }

    public async Task<string> CreateCheckoutSessionAsync(
        string priceId,
        string mode,
        string successUrl,
        string cancelUrl,
        string? customerEmail,
        string? userId,
        string? metadata,
        IReadOnlyDictionary<string, string>? sessionMetadata = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var meta = new Dictionary<string, string>
        {
            ["userId"] = userId ?? string.Empty,
            ["customMetadata"] = metadata ?? string.Empty,
        };

        if (sessionMetadata is not null)
        {
            foreach (KeyValuePair<string, string> entry in sessionMetadata)
            {
                meta[entry.Key] = entry.Value;
            }
        }

        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = ["card"],
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = 1,
                }
            ],
            Mode = mode, // "payment" or "subscription"
            UiMode = "embedded_page",
            AllowPromotionCodes = true, // lets customers enter a discount code in checkout
            ReturnUrl = successUrl.Replace("{{CHECKOUT_SESSION_ID}}", "{CHECKOUT_SESSION_ID}"),
            CustomerEmail = customerEmail,
            Metadata = meta,
        };

        // For subscriptions: add billing_cycle_anchor to Monday 15:00 Romania time
        if (mode == "subscription" && !string.IsNullOrEmpty(metadata))
        {
            long? anchor = TryParseBillingAnchor(metadata);
            if (anchor.HasValue)
            {
                options.SubscriptionData = new SessionSubscriptionDataOptions
                {
                    BillingCycleAnchor = DateTimeOffset.FromUnixTimeSeconds(anchor.Value).UtcDateTime,
                    ProrationBehavior = "none",
                };
            }
        }

        var service = new SessionService();
        RequestOptions? requestOptions = string.IsNullOrWhiteSpace(idempotencyKey)
            ? null
            : new RequestOptions { IdempotencyKey = idempotencyKey };

        Session session = await service.CreateAsync(options, requestOptions, cancellationToken);

        return session.ClientSecret;
    }

    public async Task<(string Status, string? CustomerEmail)> GetSessionStatusAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var service = new SessionService();
        Session session = await service.GetAsync(sessionId, cancellationToken: cancellationToken);
        return (session.Status, session.CustomerDetails?.Email);
    }

    public async Task<string> GetOrCreateCustomerAsync(
        string userId,
        string email,
        CancellationToken cancellationToken = default)
    {
        var service = new CustomerService();

        // Search for existing customer with this userId in metadata
        var searchOptions = new CustomerSearchOptions
        {
            Query = $"metadata['userId']:'{userId}'",
        };
        StripeSearchResult<Customer> existing = await service.SearchAsync(searchOptions, cancellationToken: cancellationToken);

        if (existing.Data.Count > 0)
        {
            return existing.Data[0].Id;
        }

        var createOptions = new CustomerCreateOptions
        {
            Email = email,
            Metadata = new Dictionary<string, string> { ["userId"] = userId },
        };

        Customer customer = await service.CreateAsync(createOptions, cancellationToken: cancellationToken);
        return customer.Id;
    }

    public async Task<string> ResolvePriceIdAsync(
        StripeCatalogItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        string cacheKey = $"{_apiKeyFingerprint}:{item.LookupKey}";
        if (PriceIdCache.TryGetValue(cacheKey, out string? cachedPriceId))
        {
            return cachedPriceId;
        }

        var priceService = new PriceService();
        StripeList<Price> existingPrices = await priceService.ListAsync(
            new PriceListOptions
            {
                Active = true,
                Limit = 1,
                LookupKeys = [item.LookupKey],
            },
            cancellationToken: cancellationToken);

        Price? price = existingPrices.Data.FirstOrDefault();

        if (price is null)
        {
            price = await CreatePriceAsync(priceService, item, cancellationToken);
        }
        else
        {
            WarnIfOutOfSync(item, price);
        }

        PriceIdCache[cacheKey] = price.Id;
        return price.Id;
    }

    private static Task<Price> CreatePriceAsync(
        PriceService priceService,
        StripeCatalogItem item,
        CancellationToken cancellationToken)
    {
        var priceMetadata = new Dictionary<string, string>(item.Metadata)
        {
            ["lookupKey"] = item.LookupKey,
        };

        var options = new PriceCreateOptions
        {
            Currency = item.Currency,
            UnitAmount = item.UnitAmountBani,
            LookupKey = item.LookupKey,
            ProductData = new PriceProductDataOptions
            {
                Name = item.ProductName,
                Metadata = priceMetadata,
            },
            Metadata = priceMetadata,
        };

        if (item.Interval is not null)
        {
            options.Recurring = new PriceRecurringOptions { Interval = item.Interval };
        }

        return priceService.CreateAsync(options, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The Stripe account decides what is actually charged; a price cannot be edited after creation.
    /// If it no longer matches the catalog, the amount in code is silently not in effect —
    /// the fix is a new lookup key, so this is worth shouting about.
    /// </summary>
    private void WarnIfOutOfSync(StripeCatalogItem item, Price price)
    {
        string? existingInterval = price.Recurring?.Interval;

        bool matches = price.UnitAmount == item.UnitAmountBani
            && string.Equals(price.Currency, item.Currency, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existingInterval, item.Interval, StringComparison.OrdinalIgnoreCase);

        if (matches)
        {
            return;
        }

        _logger.LogWarning(
            "Stripe price {PriceId} for lookup key {LookupKey} does not match the catalog: " +
            "account charges {ExistingAmount} {ExistingCurrency} ({ExistingInterval}), " +
            "catalog says {CatalogAmount} {CatalogCurrency} ({CatalogInterval}). " +
            "Prices are immutable — change the lookup key to apply a new amount.",
            price.Id,
            item.LookupKey,
            price.UnitAmount?.ToString(CultureInfo.InvariantCulture) ?? "n/a",
            price.Currency,
            existingInterval ?? "one-time",
            item.UnitAmountBani,
            item.Currency,
            item.Interval ?? "one-time");
    }

    /// <summary>
    /// Short SHA-256 prefix of the secret key, used only to scope the price cache to one account.
    /// Never logged, never returned — it must not become a way to leak the key.
    /// </summary>
    private static string Fingerprint(string apiKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash, 0, 8);
    }

    public async Task ApplyBcrDiscountAsync(
        string stripeSubscriptionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeSubscriptionId);

        string couponId = await EnsureBcrCouponAsync(cancellationToken);

        await new SubscriptionService().UpdateAsync(
            stripeSubscriptionId,
            new SubscriptionUpdateOptions
            {
                Discounts = [new SubscriptionDiscountOptions { Coupon = couponId }],
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Cuponul BCR, creat o singură dată. Id-ul e fix, deci a doua confirmare îl regăsește în loc
    /// să facă un duplicat.
    /// </summary>
    private static async Task<string> EnsureBcrCouponAsync(CancellationToken cancellationToken)
    {
        var coupons = new CouponService();

        try
        {
            Coupon existing = await coupons.GetAsync(
                Pricing.BcrDiscount.StripeCouponId,
                cancellationToken: cancellationToken);
            return existing.Id;
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Prima confirmare din contul ăsta Stripe. Orice altă eroare urcă mai departe: dacă
            // Stripe e căzut sau cheia e greșită, confirmarea trebuie să eșueze zgomotos, nu să
            // se încheie cu o reducere care n-a fost aplicată.
        }

        Coupon created = await coupons.CreateAsync(
            new CouponCreateOptions
            {
                Id = Pricing.BcrDiscount.StripeCouponId,
                Name = "RIDElance — cont BCR",
                AmountOff = Pricing.BcrDiscount.MonthlyBani,
                Currency = "ron",
                Duration = "repeating",
                DurationInMonths = Pricing.BcrDiscount.Months,
            },
            cancellationToken: cancellationToken);

        return created.Id;
    }

    public async Task<DiscountCode> CreateDiscountCodeAsync(
        NewDiscountCode code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        var couponOptions = new CouponCreateOptions
        {
            Name = code.Code,
            // "once" only discounts the first invoice of a subscription; "forever" discounts every one.
            // One-time payments are unaffected by this choice.
            Duration = code.AppliesToAllPayments ? "forever" : "once",
        };

        if (code.PercentOff.HasValue)
        {
            couponOptions.PercentOff = code.PercentOff.Value;
        }
        else
        {
            couponOptions.AmountOff = code.AmountOffBani;
            couponOptions.Currency = "ron";
        }

        Coupon coupon = await new CouponService().CreateAsync(couponOptions, cancellationToken: cancellationToken);

        var promotionOptions = new PromotionCodeCreateOptions
        {
            Promotion = new PromotionCodePromotionOptions
            {
                Type = "coupon",
                Coupon = coupon.Id,
            },
            Code = code.Code,
            MaxRedemptions = code.MaxRedemptions,
            ExpiresAt = code.ExpiresAtUtc,
        };

        PromotionCode promotionCode = await new PromotionCodeService()
            .CreateAsync(promotionOptions, cancellationToken: cancellationToken);

        return ToDiscountCode(promotionCode, coupon);
    }

    public async Task<IReadOnlyList<DiscountCode>> ListDiscountCodesAsync(
        CancellationToken cancellationToken = default)
    {
        StripeList<PromotionCode> codes = await new PromotionCodeService().ListAsync(
            new PromotionCodeListOptions
            {
                Limit = 100,
                Expand = ["data.promotion.coupon"],
            },
            cancellationToken: cancellationToken);

        return [.. codes.Data.Select(promotionCode => ToDiscountCode(promotionCode, promotionCode.Promotion?.Coupon))];
    }

    public async Task<DiscountCode> SetDiscountCodeActiveAsync(
        string promotionCodeId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        PromotionCode promotionCode = await new PromotionCodeService().UpdateAsync(
            promotionCodeId,
            new PromotionCodeUpdateOptions
            {
                Active = active,
                Expand = ["promotion.coupon"],
            },
            cancellationToken: cancellationToken);

        return ToDiscountCode(promotionCode, promotionCode.Promotion?.Coupon);
    }

    private static DiscountCode ToDiscountCode(PromotionCode promotionCode, Coupon? coupon) =>
        new(
            promotionCode.Id,
            promotionCode.Code,
            coupon?.AmountOff,
            coupon?.PercentOff,
            coupon?.Currency,
            promotionCode.MaxRedemptions,
            promotionCode.TimesRedeemed,
            promotionCode.Active,
            string.Equals(coupon?.Duration, "forever", StringComparison.OrdinalIgnoreCase),
            promotionCode.ExpiresAt,
            promotionCode.Created);

    public Stripe.Event? ConstructWebhookEvent(string payload, string stripeSignatureHeader)
    {
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            // Development only — the constructor refuses to start without a secret anywhere else.
            _logger.LogWarning("Stripe webhook signature verification is skipped: no webhook secret configured.");
            return EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);
        }

        try
        {
            return EventUtility.ConstructEvent(payload, stripeSignatureHeader, _webhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return null;
        }
    }

    /// <summary>
    /// Parses a Unix timestamp from the metadata string "billingAnchor:{timestamp}".
    /// </summary>
    private static long? TryParseBillingAnchor(string metadata)
    {
        // Metadata format: "plan:solo|billingAnchor:12345678" or "plan:solo"
        if (string.IsNullOrEmpty(metadata))
        {
            return null;
        }

        string[] parts = metadata.Split('|');
        foreach (string part in parts)
        {
            const string prefix = "billingAnchor:";
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(part[prefix.Length..], out long ts))
            {
                return ts;
            }
        }

        return null;
    }
}
