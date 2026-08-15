using Application.Abstractions.Messaging;
using Application.PfaConnections;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaConnections;

internal sealed class PfaConnectionsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("pfa/connections")
            .RequireAuthorization()
            .WithTags("PfaConnections");

        group.MapGet("oblio", async (
            IQueryHandler<GetOblioConnectionQuery, OblioConnectionResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<OblioConnectionResponse> result = await handler.Handle(
                new GetOblioConnectionQuery(),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }
}
