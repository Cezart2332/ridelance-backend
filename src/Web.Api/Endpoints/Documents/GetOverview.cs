using Application.Abstractions.Messaging;
using Application.Documents.Overview;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Documents;

internal sealed class GetOverview : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("documents/overview", async (
            string? group,
            IQueryHandler<GetDocumentsOverviewQuery, DocumentsOverviewResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<DocumentsOverviewResponse> result = await handler.Handle(
                new GetDocumentsOverviewQuery(group),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Documents);
    }
}
