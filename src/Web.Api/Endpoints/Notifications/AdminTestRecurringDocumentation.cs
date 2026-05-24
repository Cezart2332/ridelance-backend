using Application.Abstractions.Messaging;
using Application.Notifications.RecurringDocumentation;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Notifications;

internal sealed class AdminTestRecurringDocumentation : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("notifications/admin/test-recurring-documentation", async (
            ICommandHandler<AdminTestRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult> handler,
            CancellationToken cancellationToken) =>
        {
            Result<SendRecurringDocumentationNotificationsResult> result =
                await handler.Handle(new AdminTestRecurringDocumentationNotificationsCommand(), cancellationToken);

            return result.Match(
                value => Results.Ok(new
                {
                    usersNotified = value.UsersNotified,
                    inAppCreated = value.InAppCreated,
                    pushSent = value.PushSent,
                }),
                CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Notifications);
    }
}
