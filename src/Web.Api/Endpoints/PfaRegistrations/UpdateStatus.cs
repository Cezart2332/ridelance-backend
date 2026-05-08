using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.UpdateStatus;
using Domain.PfaRegistrations;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

internal sealed class UpdateStatus : IEndpoint
{
    public sealed record Request(string Status, string? ReviewNote, string? Cui, Guid? DocumentId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("pfa-registrations/{id:guid}/status", async (
            Guid id,
            Request request,
            IUserContext userContext,
            ICommandHandler<UpdatePfaRegistrationStatusCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<PfaRegistrationStatus>(request.Status, ignoreCase: true, out PfaRegistrationStatus status))
            {
                return Results.BadRequest("Invalid status value.");
            }

            var command = new UpdatePfaRegistrationStatusCommand(
                id, 
                userContext.UserId, 
                status, 
                request.ReviewNote,
                request.Cui,
                request.DocumentId);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);
    }
}
