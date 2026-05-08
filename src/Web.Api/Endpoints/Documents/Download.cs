using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Documents.Download;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Documents;

internal sealed class Download : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("documents/{id:guid}/download", async (
            Guid id,
            IUserContext userContext,
            IQueryHandler<DownloadDocumentQuery, DownloadDocumentResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new DownloadDocumentQuery(id, userContext.UserId);

            Result<DownloadDocumentResponse> result = await handler.Handle(query, cancellationToken);

            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            DownloadDocumentResponse doc = result.Value;

            return Results.File(
                doc.FileStream,
                doc.ContentType,
                doc.FileName);
        })
        .RequireAuthorization()
        .WithTags(Tags.Documents);
    }
}
