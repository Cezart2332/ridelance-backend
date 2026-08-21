using Application.Abstractions.Messaging;
using Application.Companies.Queries.GetPublicCompany;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

internal sealed class GetPublicCompany : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("companies/{slug}/public", async (
            string slug,
            IQueryHandler<GetPublicCompanyQuery, PublicCompanyDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<PublicCompanyDto> result = await handler.Handle(
                new GetPublicCompanyQuery(slug),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .AllowAnonymous()
        .WithTags(Tags.Companies);
    }
}
