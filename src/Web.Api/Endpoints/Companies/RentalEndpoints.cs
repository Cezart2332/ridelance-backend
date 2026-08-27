using Application.Abstractions.Messaging;
using Application.Rentals;
using Application.Rentals.Commands.CloseRental;
using Application.Rentals.Commands.CreateRental;
using Application.Rentals.Commands.SaveRentalDefaults;
using Application.Rentals.Commands.UpdateRental;
using Application.Rentals.Queries.GetRentalDefaults;
using Application.Rentals.Documents;
using Application.Rentals.Queries.GetRentalDocuments;
using Application.Rentals.Queries.GetRentals;
using Application.Rentals.Queries.GetTenants;
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

internal sealed class UpdateRental : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("rentals/{id:guid}", async (
            Guid id,
            UpdateRentalRequest body,
            ICommandHandler<UpdateRentalCommand> handler,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);

            Result result = await handler.Handle(
                new UpdateRentalCommand(
                    id,
                    body.StartAtUtc,
                    body.EndAtUtc,
                    body.WeeklyRentBani,
                    body.DepositBani,
                    body.OtherCostsBani,
                    body.HasKmLimit,
                    body.MileageLimit,
                    body.ExtraKmCostBani,
                    body.FuelRule,
                    body.FuelLevelAtPickup,
                    body.StartMileage,
                    body.Accessories,
                    body.AccessoriesOther,
                    body.Notes),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }

    internal sealed record UpdateRentalRequest(
        DateTime StartAtUtc,
        DateTime EndAtUtc,
        long WeeklyRentBani,
        long DepositBani,
        long OtherCostsBani,
        bool HasKmLimit,
        int? MileageLimit,
        long ExtraKmCostBani,
        string? FuelRule,
        string? FuelLevelAtPickup,
        int? StartMileage,
        IReadOnlyList<string>? Accessories,
        string? AccessoriesOther,
        string? Notes);
}

internal sealed class GetTenants : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("rentals/tenants", async (
            IQueryHandler<GetTenantsQuery, List<TenantDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<TenantDto>> result = await handler.Handle(new GetTenantsQuery(), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class RentalDefaults : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("rentals/defaults", async (
            IQueryHandler<GetRentalDefaultsQuery, RentalDefaultsDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<RentalDefaultsDto> result = await handler.Handle(new GetRentalDefaultsQuery(), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);

        app.MapPut("rentals/defaults", async (
            RentalDefaultsDto body,
            ICommandHandler<SaveRentalDefaultsCommand, RentalDefaultsDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<RentalDefaultsDto> result = await handler.Handle(
                new SaveRentalDefaultsCommand(body),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}

internal sealed class RentalDocuments : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("rentals/{id:guid}/documents", async (
            Guid id,
            IQueryHandler<GetRentalDocumentsQuery, List<GeneratedDocumentDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<GeneratedDocumentDto>> result = await handler.Handle(
                new GetRentalDocumentsQuery(id), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);

        app.MapPost("rentals/{id:guid}/documents", async (
            Guid id,
            GenerateDocumentRequest body,
            ICommandHandler<GenerateRentalDocumentCommand, GeneratedDocumentDto> handler,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);

            if (!Enum.TryParse(body.Type, ignoreCase: true, out RentalDocumentType type))
            {
                return Results.BadRequest(new { detail = "Tip de document necunoscut." });
            }

            Result<GeneratedDocumentDto> result = await handler.Handle(
                new GenerateRentalDocumentCommand(id, type), cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }

    internal sealed record GenerateDocumentRequest(string Type);
}
