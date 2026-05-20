using Application.Abstractions.Messaging;
using Application.Payments.CreatePublicServiceCheckout;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Payments;

internal sealed class CreatePublicServiceCheckout : IEndpoint
{
    public sealed record Request(
        string ServiceKey,
        string CustomerName,
        string CustomerEmail,
        string CustomerPhone,
        string? SuccessUrl = null,
        string? CancelUrl = null);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("payments/public/service-checkout", async (
            Request request,
            ICommandHandler<CreatePublicServiceCheckoutCommand, string> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreatePublicServiceCheckoutCommand(
                request.ServiceKey,
                request.CustomerName,
                request.CustomerEmail,
                request.CustomerPhone,
                request.SuccessUrl,
                request.CancelUrl);

            Result<string> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                url => Results.Ok(new { url }),
                CustomResults.Problem);
        })
        .WithTags(Tags.Payments);
    }
}
