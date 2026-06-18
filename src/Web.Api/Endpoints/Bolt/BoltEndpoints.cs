using Application.Abstractions.Messaging;
using Application.Bolt.Commands;
using Application.Bolt.Queries;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

#pragma warning disable IDE0007, IDE0008

namespace Web.Api.Endpoints.Bolt;

internal sealed class BoltEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("bolt/debug", async (
            Application.Abstractions.Data.IApplicationDbContext db,
            CancellationToken ct) =>
        {
            var list = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.BoltIntegrations, ct);
            return Results.Ok(list);
        }).AllowAnonymous();

        var group = app.MapGroup("bolt")
            .RequireAuthorization()
            .WithTags("Bolt Integration");

        group.MapGet("integration", async (
            IQueryHandler<GetBoltIntegrationQuery, BoltIntegrationResponse?> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetBoltIntegrationQuery();
            Result<BoltIntegrationResponse?> result = await handler.Handle(query, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("integration", async (
            ConfigureBoltIntegrationRequest request,
            ICommandHandler<ConfigureBoltIntegrationCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ConfigureBoltIntegrationCommand(request.ClientId, request.ClientSecret);
            Result<Guid> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("orders", async (
            int? limit,
            int? offset,
            IQueryHandler<GetBoltOrdersQuery, BoltOrdersResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetBoltOrdersQuery(limit, offset);
            Result<BoltOrdersResponse> result = await handler.Handle(query, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("dashboard", async (
            string? period,
            int? year,
            int? month,
            IQueryHandler<GetBoltDashboardQuery, BoltDashboardResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetBoltDashboardQuery(period, year, month);
            Result<BoltDashboardResponse> result = await handler.Handle(query, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("sync", async (
            ICommandHandler<SyncBoltOrdersCommand, bool> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new SyncBoltOrdersCommand();
            Result<bool> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }
}

public sealed record ConfigureBoltIntegrationRequest(string ClientId, string ClientSecret);
