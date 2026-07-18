using Application.Abstractions.Messaging;
using Application.Admin.GoCardless;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Admin;

internal sealed class AdminGoCardlessEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/gocardless/status", async (
            IQueryHandler<GetGoCardlessStatusQuery, GoCardlessStatusResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<GoCardlessStatusResponse> result =
                await handler.Handle(new GetGoCardlessStatusQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewServiceOrders)
        .WithTags(Tags.Admin);
    }
}
