using Domain.Notifications;
using Domain.Users;

namespace Application.Abstractions.Notifications;

public interface IWebPushService
{
#pragma warning disable CA1054
    Task SendPushNotificationAsync(PushSubscription subscription, string title, string body, string? url = null, CancellationToken cancellationToken = default);
#pragma warning restore CA1054
}
