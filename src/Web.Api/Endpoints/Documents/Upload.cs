using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Documents.Upload;
using Domain.Documents;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Documents;

internal sealed class Upload : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("documents/upload", async (
            [FromForm] IFormFile file,
            [FromForm] string category,
            [FromForm] string? pfaRegistrationId,
            [FromForm] string? userId,
            HttpContext httpContext,
            IUserContext userContext,
            ICommandHandler<UploadDocumentCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            Guid targetUserId = userContext.UserId;
            if (userId != null && 
                (httpContext.User.IsInRole("Admin") || httpContext.User.IsInRole("Contabil")) && 
                Guid.TryParse(userId, out Guid gUserId))
            {
                targetUserId = gUserId;
            }
            if (!Enum.TryParse<DocumentCategory>(category, ignoreCase: true, out DocumentCategory docCategory))
            {
                docCategory = DocumentCategory.Other;
            }

            Guid? parsedPfaId = Guid.TryParse(pfaRegistrationId, out Guid g) ? g : null;

            using Stream stream = file.OpenReadStream();

            var command = new UploadDocumentCommand(
                targetUserId,
                parsedPfaId,
                docCategory,
                file.FileName,
                file.ContentType,
                stream,
                file.Length);

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            return Results.Ok(new { documentId = result.Value });
        })
        .RequireAuthorization()
        .DisableAntiforgery()
        .WithTags(Tags.Documents);
    }
}
