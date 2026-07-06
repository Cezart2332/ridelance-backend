using Application.Abstractions.Messaging;
using Application.PfaRegistrations.GetContabilStats;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

internal sealed class GetContabilStats : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("pfa-registrations/contabil-stats", async (
            int? year,
            int? month,
            IQueryHandler<GetContabilStatsQuery, ContabilStatsResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetContabilStatsQuery(year, month);
            Result<ContabilStatsResponse> result = await handler.Handle(query, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewAssignedClients)
        .WithTags(Tags.PfaRegistrations);
    }
}
