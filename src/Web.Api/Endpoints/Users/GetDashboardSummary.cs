using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Users.GetDashboardSummary;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class GetDashboardSummary : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("users/dashboard-summary", async (
            IUserContext userContext,
            IQueryHandler<GetDashboardSummaryQuery, DashboardSummaryResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetDashboardSummaryQuery(userContext.UserId);
            Result<DashboardSummaryResponse> result = await handler.Handle(query, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Users);
    }
}
