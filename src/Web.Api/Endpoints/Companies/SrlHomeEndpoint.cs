using Application.Abstractions.Messaging;
using Application.SrlDashboard;
using Application.SrlDashboard.Queries.GetSrlHome;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

internal sealed class GetSrlHome : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("srl/home", async (
            IQueryHandler<GetSrlHomeQuery, SrlHomeDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<SrlHomeDto> result = await handler.Handle(new GetSrlHomeQuery(), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Companies);
    }
}
