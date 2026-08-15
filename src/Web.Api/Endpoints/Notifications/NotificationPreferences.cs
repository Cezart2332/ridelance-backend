using Application.Abstractions.Messaging;
using Application.Notifications.Preferences;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Notifications;

internal sealed class NotificationPreferences : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("notifications/preferences")
            .RequireAuthorization()
            .WithTags(Tags.Notifications);

        group.MapGet(string.Empty, async (
            IQueryHandler<GetNotificationPreferencesQuery, NotificationPreferencesResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<NotificationPreferencesResponse> result = await handler.Handle(
                new GetNotificationPreferencesQuery(),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPut(string.Empty, async (
            UpdateNotificationPreferencesRequest request,
            ICommandHandler<UpdateNotificationPreferencesCommand, NotificationPreferencesResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<NotificationPreferencesResponse> result = await handler.Handle(
                new UpdateNotificationPreferencesCommand(request.Items),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }

    internal sealed record UpdateNotificationPreferencesRequest(List<NotificationPreferenceUpdate> Items);
}
