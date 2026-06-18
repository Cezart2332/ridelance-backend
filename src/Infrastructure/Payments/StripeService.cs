using Application.Abstractions.Services;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;

namespace Infrastructure.Payments;

/// <summary>
/// Stripe service implementation using Stripe.net SDK.
/// </summary>
internal sealed class StripeService : IStripeService
{
    private readonly string _webhookSecret;

    public StripeService(IConfiguration configuration)
    {
        string apiKey = configuration["Stripe:SecretKey"]
            ?? Environment.GetEnvironmentVariable("Stripe__SecretKey")
            ?? throw new InvalidOperationException("Stripe SecretKey is not configured.");

        _webhookSecret = configuration["Stripe:WebhookSecret"]
            ?? Environment.GetEnvironmentVariable("Stripe__WebhookSecret")
            ?? string.Empty;

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
        Session session = await service.CreateAsync(options, cancellationToken: cancellationToken);

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

    public async Task<string> GetOrCreateRecurringPriceAsync(
        string lookupKey,
        string productName,
        long unitAmount,
        string currency,
        string interval,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var priceService = new PriceService();
        StripeList<Price> existingPrices = await priceService.ListAsync(
            new PriceListOptions
            {
                Active = true,
                Limit = 1,
                LookupKeys = [lookupKey],
            },
            cancellationToken: cancellationToken);

        Price? existingPrice = existingPrices.Data.FirstOrDefault();
        if (existingPrice is not null)
        {
            return existingPrice.Id;
        }

        Dictionary<string, string> priceMetadata = metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(metadata);

        priceMetadata["lookupKey"] = lookupKey;

        Price price = await priceService.CreateAsync(
            new PriceCreateOptions
            {
                Currency = currency,
                UnitAmount = unitAmount,
                LookupKey = lookupKey,
                Recurring = new PriceRecurringOptions
                {
                    Interval = interval,
                },
                ProductData = new PriceProductDataOptions
                {
                    Name = productName,
                    Metadata = priceMetadata,
                },
                Metadata = priceMetadata,
            },
            cancellationToken: cancellationToken);

        return price.Id;
    }

    public async Task<string> GetOrCreateOneTimePriceAsync(
        string lookupKey,
        string productName,
        long unitAmount,
        string currency,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var priceService = new PriceService();
        StripeList<Price> existingPrices = await priceService.ListAsync(
            new PriceListOptions
            {
                Active = true,
                Limit = 1,
                LookupKeys = [lookupKey],
            },
            cancellationToken: cancellationToken);

        Price? existingPrice = existingPrices.Data.FirstOrDefault();
        if (existingPrice is not null)
        {
            return existingPrice.Id;
        }

        Dictionary<string, string> priceMetadata = metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(metadata);

        priceMetadata["lookupKey"] = lookupKey;

        Price price = await priceService.CreateAsync(
            new PriceCreateOptions
            {
                Currency = currency,
                UnitAmount = unitAmount,
                LookupKey = lookupKey,
                ProductData = new PriceProductDataOptions
                {
                    Name = productName,
                    Metadata = priceMetadata,
                },
                Metadata = priceMetadata,
            },
            cancellationToken: cancellationToken);

        return price.Id;
    }

    public Stripe.Event? ConstructWebhookEvent(string payload, string stripeSignatureHeader)
    {
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            // No webhook secret configured — skip verification in dev
            return EventUtility.ParseEvent(payload, throwOnApiVersionMismatch: false);
        }

        try
        {
            return EventUtility.ConstructEvent(payload, stripeSignatureHeader, _webhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            Console.WriteLine($"Stripe Webhook Error: {ex.Message}");
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
