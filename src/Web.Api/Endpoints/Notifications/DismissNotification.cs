using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Notifications.Dismiss;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Notifications;

internal sealed class DismissNotification : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("notifications/{id:guid}", async (
            Guid id, IUserContext userContext,
            ICommandHandler<DismissNotificationCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new DismissNotificationCommand(userContext.UserId, id), cancellationToken);
            return result.Match(() => Results.NoContent(), CustomResults.Problem);
        }).RequireAuthorization().WithTags(Tags.Notifications);
    }
}
