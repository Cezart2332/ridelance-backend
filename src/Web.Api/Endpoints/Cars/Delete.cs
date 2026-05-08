using Application.Abstractions.Messaging;
using Application.Cars.Commands.DeleteCar;
using Application.Cars.Commands.ToggleCarActive;
using SharedKernel;
using Web.Api.Infrastructure;
using Infrastructure.Authorization;

namespace Web.Api.Endpoints.Cars;

internal sealed class Delete : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("cars/{id:guid}", async (
            Guid id,
            ICommandHandler<DeleteCarCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new DeleteCarCommand(id), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization(Permissions.ManageCars)
        .WithTags(Tags.Cars);
    }
}

internal sealed class ToggleActive : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("cars/{id:guid}/toggle-active", async (
            Guid id,
            ICommandHandler<ToggleCarActiveCommand, bool> handler,
            CancellationToken cancellationToken) =>
        {
            Result<bool> result = await handler.Handle(new ToggleCarActiveCommand(id), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(new { active = result.Value });
        })
        .RequireAuthorization(Permissions.ManageCars)
        .WithTags(Tags.Cars);
    }
}
