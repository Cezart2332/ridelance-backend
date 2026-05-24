using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Notifications.RecurringDocumentation;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Notifications;

internal sealed class EnsureRecurringDocumentation : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("notifications/recurring-documentation/ensure", async (
            IUserContext userContext,
            ICommandHandler<EnsureRecurringDocumentationNotificationCommand, EnsureRecurringDocumentationNotificationResult> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new EnsureRecurringDocumentationNotificationCommand(userContext.UserId);

            Result<EnsureRecurringDocumentationNotificationResult> result =
                await handler.Handle(command, cancellationToken);

            return result.Match(
                value => Results.Ok(new
                {
                    created = value.Created,
                    notificationId = value.NotificationId,
                    pushSent = value.PushSent,
                }),
                CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Notifications);
    }
}
