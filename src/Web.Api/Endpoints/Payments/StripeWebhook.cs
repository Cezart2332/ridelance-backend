using Application.Abstractions.Messaging;
using Application.Payments.HandleWebhook;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Payments;

/// <summary>
/// Stripe webhook endpoint. Must NOT use authorization (Stripe calls this without a JWT).
/// The signature is validated inside the command handler via Stripe's webhook secret.
/// </summary>
internal sealed class StripeWebhook : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("payments/webhook/stripe", async (
            HttpRequest httpRequest,
            ICommandHandler<HandleStripeWebhookCommand> handler,
            CancellationToken cancellationToken) =>
        {
            // Read raw body — required for Stripe signature verification
            using var reader = new StreamReader(httpRequest.Body);
            string payload = await reader.ReadToEndAsync(cancellationToken);

            string stripeSignature = httpRequest.Headers["Stripe-Signature"].ToString();

            var command = new HandleStripeWebhookCommand(payload, stripeSignature);
            Result result = await handler.Handle(command, cancellationToken);

            return result.IsSuccess
                ? Results.Ok()
                : Results.Problem(detail: "Webhook processing failed.", statusCode: 400);
        })
        .AllowAnonymous()  // Stripe calls this, no JWT
        .WithTags(Tags.Payments)
        .DisableAntiforgery();
    }
}
