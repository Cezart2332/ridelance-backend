using Application.Abstractions.Messaging;
using Application.PfaRegistrations.GetAll;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

internal sealed class GetAll : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("pfa-registrations", async (
            int? page,
            int? pageSize,
            IQueryHandler<GetAllPfaRegistrationsQuery, PfaRegistrationListResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetAllPfaRegistrationsQuery(page ?? 1, pageSize ?? 20);

            Result<PfaRegistrationListResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:view")
        .WithTags(Tags.PfaRegistrations);
    }
}
