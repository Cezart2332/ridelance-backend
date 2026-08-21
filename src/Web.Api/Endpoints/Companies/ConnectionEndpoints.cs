using Application.Abstractions.Messaging;
using Application.Connections.Queries.GetConnections;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

internal sealed class GetConnections : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("connections", async (
            IQueryHandler<GetConnectionsQuery, List<IntegrationDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<IntegrationDto>> result = await handler.Handle(new GetConnectionsQuery(), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}
