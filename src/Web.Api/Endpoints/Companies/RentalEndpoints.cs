using Application.Abstractions.Messaging;
using Application.Rentals;
using Application.Rentals.Commands.CloseRental;
using Application.Rentals.Commands.CreateRental;
using Application.Rentals.Queries.GetRentals;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

internal sealed class GetRentals : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("rentals", async (
            IQueryHandler<GetRentalsQuery, RentalOverviewDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<RentalOverviewDto> result = await handler.Handle(new GetRentalsQuery(), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class CreateRental : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("rentals", async (
            CreateRentalCommand command,
            ICommandHandler<CreateRentalCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            Result<Guid> result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(new { id = result.Value });
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class CloseRental : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("rentals/{id:guid}/close", async (
            Guid id,
            CloseRentalRequest? body,
            ICommandHandler<CloseRentalCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(
                new CloseRentalCommand(id, body?.EndMileage),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }

    internal sealed record CloseRentalRequest(int? EndMileage);
}
