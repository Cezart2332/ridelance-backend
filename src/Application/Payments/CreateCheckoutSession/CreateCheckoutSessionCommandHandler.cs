using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Payments.CreateCheckoutSession;

internal sealed class CreateCheckoutSessionCommandHandler(
    IStripeService stripeService,
    IConfiguration configuration)
    : ICommandHandler<CreateCheckoutSessionCommand, string>
{
    public async Task<Result<string>> Handle(
        CreateCheckoutSessionCommand command,
        CancellationToken cancellationToken)
    {
#pragma warning disable S1075 // URIs should not be hardcoded
        string baseUrl = configuration["App:BaseUrl"] ?? "http://localhost:5173";
#pragma warning restore S1075

        // Build success/cancel URLs
        // Success URL returns user to the subscription page or dashboard
        string successUrl = command.Mode == "subscription"
            ? $"{baseUrl}/inregistrare/succes?session_id={{CHECKOUT_SESSION_ID}}&plan={command.Plan}"
            : $"{baseUrl}/inregistrare/abonament?pfa_paid=true&session_id={{CHECKOUT_SESSION_ID}}";

        string cancelUrl = command.Mode == "subscription"
            ? $"{baseUrl}/inregistrare/abonament"
            : $"{baseUrl}/inregistrare/pfa";

        // Build metadata: include plan name + billing anchor for subscriptions
        string metadata = string.Empty;
        if (command.Mode == "subscription")
        {
            metadata = command.BillingAnchorUnix.HasValue
                ? $"plan:{command.Plan}|billingAnchor:{command.BillingAnchorUnix.Value}"
                : $"plan:{command.Plan}";
        }

        string sessionUrl = await stripeService.CreateCheckoutSessionAsync(
            command.PriceId,
            command.Mode,
            successUrl,
            cancelUrl,
            command.UserEmail,
            command.UserId.ToString(),
            metadata,
            cancellationToken);

        return sessionUrl;
    }
}
