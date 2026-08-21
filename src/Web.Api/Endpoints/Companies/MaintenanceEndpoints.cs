using Application.Abstractions.Messaging;
using Application.Maintenance;
using Application.Maintenance.Commands.AddMaintenanceEntry;
using Application.Maintenance.Commands.DeleteMaintenanceEntry;
using Application.Maintenance.Queries.GetMaintenanceEntries;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

internal sealed class GetMaintenance : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("maintenance", async (
            Guid? carId,
            IQueryHandler<GetMaintenanceEntriesQuery, MaintenanceOverviewDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<MaintenanceOverviewDto> result = await handler.Handle(
                new GetMaintenanceEntriesQuery(carId),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class AddMaintenance : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("maintenance", async (
            AddMaintenanceEntryCommand command,
            ICommandHandler<AddMaintenanceEntryCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            Result<Guid> result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(new { id = result.Value });
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class DeleteMaintenance : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("maintenance/{id:guid}", async (
            Guid id,
            ICommandHandler<DeleteMaintenanceEntryCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new DeleteMaintenanceEntryCommand(id), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}
