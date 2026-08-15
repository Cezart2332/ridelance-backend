using Application.Abstractions.Messaging;
using Application.Admin.Banking;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Admin;

internal sealed class AdminBankingEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/banking/status", async (
            IQueryHandler<GetUnclaimedConnectionsQuery, BankingStatusResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<BankingStatusResponse> result = await handler.Handle(
                new GetUnclaimedConnectionsQuery(),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageClientIncome)
        .WithTags("Admin");
    }
}
