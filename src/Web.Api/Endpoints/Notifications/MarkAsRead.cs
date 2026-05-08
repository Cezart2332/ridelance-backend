using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Notifications.MarkAsRead;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Notifications;

internal sealed class MarkAsRead : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("notifications/{id}/read", async (
            Guid id,
            IUserContext userContext,
            ICommandHandler<MarkAsReadCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new MarkAsReadCommand(userContext.UserId, id);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(() => Results.NoContent(), CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Notifications);

        app.MapPut("notifications/read-all", async (
            IUserContext userContext,
            ICommandHandler<MarkAsReadCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new MarkAsReadCommand(userContext.UserId, null);

            Result result = await handler.Handle(command, cancellationToken);

            return result.Match(() => Results.NoContent(), CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Notifications);
    }
}
