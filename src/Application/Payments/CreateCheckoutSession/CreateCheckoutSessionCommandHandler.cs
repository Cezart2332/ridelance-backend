using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Payments.CreateCheckoutSession;

internal sealed class CreateCheckoutSessionCommandHandler(
    IStripeService stripeService,
    IApplicationDbContext context,
    IConfiguration configuration)
    : ICommandHandler<CreateCheckoutSessionCommand, string>
{
    public async Task<Result<string>> Handle(
        CreateCheckoutSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.IsPlanChange)
        {
            Domain.Payments.UserSubscription? subscription = await context.UserSubscriptions
                .Where(s => s.UserId == command.UserId)
                .OrderByDescending(s => s.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (subscription is not null && subscription.Status == Domain.Payments.SubscriptionStatus.ActivePendingBilling)
            {
                return Result.Failure<string>(Error.Problem(
                    "Subscription.PendingChange",
                    "Ai deja o schimbare de abonament programată pentru lunea viitoare. Poți schimba abonamentul doar o dată pe săptămână."));
            }
        }

#pragma warning disable S1075 // URIs should not be hardcoded
        string baseUrl = configuration["App:BaseUrl"] ?? throw new InvalidOperationException("App:BaseUrl is missing in configuration.");
#pragma warning restore S1075

        // Build success/cancel URLs
        // Subscription: after paying, user must still complete the PFA step
        // Payment (Infiintare PFA): goes to the registration success page
        string successUrl = command.SuccessUrl ?? (command.Mode == "subscription"
            ? $"{baseUrl}/inregistrare/pfa?subscribed=1&session_id={{CHECKOUT_SESSION_ID}}&plan={command.Plan}"
            : $"{baseUrl}/inregistrare/succes?session_id={{CHECKOUT_SESSION_ID}}");

        string cancelUrl = command.CancelUrl ?? (command.Mode == "subscription"
            ? $"{baseUrl}/inregistrare/abonament"
            : $"{baseUrl}/inregistrare/pfa");

        // Build metadata: include plan/service name + billing anchor for subscriptions
        string metadata = command.Mode == "subscription" && command.BillingAnchorUnix.HasValue
            ? $"plan:{command.Plan}|billingAnchor:{command.BillingAnchorUnix.Value}"
            : $"plan:{command.Plan}";



        IReadOnlyDictionary<string, string>? sessionMetadata = command.IsPlanChange
            ? new Dictionary<string, string> { ["isPlanChange"] = "true" }
            : null;

        string sessionUrl = await stripeService.CreateCheckoutSessionAsync(
            command.PriceId,
            command.Mode,
            successUrl,
            cancelUrl,
            command.UserEmail,
            command.UserId.ToString(),
            metadata,
            sessionMetadata,
            cancellationToken);

        return sessionUrl;
    }
}
