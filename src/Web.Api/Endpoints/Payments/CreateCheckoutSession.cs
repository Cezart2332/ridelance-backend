using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Payments.CreateCheckoutSession;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Payments;

internal sealed class CreateCheckoutSession : IEndpoint
{
    public sealed record Request(
        string PriceId,
        string Mode,          // "payment" or "subscription"
        string Plan,          // e.g. "solo", "start", "pro", "infiintare_pfa"
        long? BillingAnchorUnix,
        string? SuccessUrl = null,
        string? CancelUrl = null);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("payments/checkout-session", async (
            Request request,
            IUserContext userContext,
            IApplicationDbContext dbContext,
            ICommandHandler<CreateCheckoutSessionCommand, string> handler,
            CancellationToken cancellationToken) =>
        {
            // Load user email
            string? email = await dbContext.Users
                .Where(u => u.Id == userContext.UserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

            var command = new CreateCheckoutSessionCommand(
                userContext.UserId,
                email ?? string.Empty,
                request.PriceId,
                request.Mode,
                request.Plan,
                request.BillingAnchorUnix,
                request.SuccessUrl,
                request.CancelUrl);

            Result<string> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                url => Results.Ok(new { url }),
                CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Payments);
    }
}
