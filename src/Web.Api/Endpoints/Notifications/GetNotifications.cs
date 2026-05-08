using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Notifications;
using Application.Notifications.GetNotifications;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Notifications;

internal sealed class GetNotifications : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("notifications", async (
            IUserContext userContext,
            IQueryHandler<GetNotificationsQuery, IReadOnlyList<NotificationResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetNotificationsQuery(userContext.UserId);

            Result<IReadOnlyList<NotificationResponse>> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Notifications);
    }
}
