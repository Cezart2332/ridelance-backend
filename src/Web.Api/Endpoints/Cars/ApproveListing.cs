using Application.Abstractions.Messaging;
using Application.Cars.Commands.ApproveCarListing;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Cars;

internal sealed class ApproveListing : IEndpoint
{
    public sealed record Request(bool Approve);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("cars/{id:guid}/approval", async (
            Guid id,
            Request request,
            ICommandHandler<ApproveCarListingCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new ApproveCarListingCommand(id, request.Approve), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization(Permissions.ManageCars)
        .WithTags(Tags.Cars);
    }
}
