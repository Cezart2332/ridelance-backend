using Application.Abstractions.Messaging;
using Application.Payments.Queries.GetServiceOrdersAdmin;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Payments;

internal sealed class GetServiceOrdersAdmin : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("payments/service-orders", async (
            string? status,
            IQueryHandler<GetServiceOrdersAdminQuery, List<ServiceOrderAdminDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<ServiceOrderAdminDto>> result = await handler.Handle(
                new GetServiceOrdersAdminQuery(status),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization(Permissions.ViewServiceOrders)
        .WithTags(Tags.Payments);
    }
}
