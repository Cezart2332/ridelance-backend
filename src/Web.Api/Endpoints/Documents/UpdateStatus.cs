using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Documents.UpdateStatus;
using Domain.Documents;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Documents;

internal sealed class UpdateStatus : IEndpoint
{
    public sealed class UpdateStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("documents/{id}/status", async (
            Guid id,
            [FromBody] UpdateStatusRequest request,
            IUserContext userContext,
            ICommandHandler<UpdateDocumentStatusCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.Status, true, out DocumentStatus parsedStatus))
            {
                return Results.BadRequest(new { Error = "Invalid status value." });
            }

            var command = new UpdateDocumentStatusCommand(id, userContext.UserId, parsedStatus);

            Result result = await handler.Handle(command, cancellationToken);

            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            return Results.Ok();
        })
        .RequireAuthorization()
        .WithTags(Tags.Documents);
    }
}
