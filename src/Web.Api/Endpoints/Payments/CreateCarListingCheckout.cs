using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Payments.CreateCarListingCheckout;
using Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Payments;

internal sealed class CreateCarListingCheckout : IEndpoint
{
    public sealed record Request(
        Guid CarId,
        string? SuccessUrl = null,
        string? CancelUrl = null);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("payments/car-listing-checkout", async (
            Request request,
            IUserContext userContext,
            IApplicationDbContext dbContext,
            ICommandHandler<CreateCarListingCheckoutCommand, string> handler,
            CancellationToken cancellationToken) =>
        {
            string? email = await dbContext.Users
                .Where(u => u.Id == userContext.UserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

            var command = new CreateCarListingCheckoutCommand(
                userContext.UserId,
                email ?? string.Empty,
                request.CarId,
                request.SuccessUrl,
                request.CancelUrl);

            Result<string> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                url => Results.Ok(new { url }),
                CustomResults.Problem);
        })
        .RequireAuthorization(Permissions.ManageOwnCars)
        .WithTags(Tags.Payments);
    }
}
