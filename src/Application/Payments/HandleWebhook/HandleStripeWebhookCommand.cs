using Application.Abstractions.Messaging;
using Stripe;

namespace Application.Payments.HandleWebhook;

/// <summary>
/// Processes a raw Stripe webhook event payload.
/// </summary>
public sealed record HandleStripeWebhookCommand(
    string Payload,
    string StripeSignatureHeader
) : ICommand;
