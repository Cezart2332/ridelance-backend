using Application.Abstractions.Notifications;
using Domain.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using WebPush;

namespace Infrastructure.Notifications;

internal sealed class WebPushService(IConfiguration configuration, ILogger<WebPushService> logger) : IWebPushService
{
#pragma warning disable CA1054
    public async Task SendPushNotificationAsync(Domain.Users.PushSubscription subscription, string title, string body, string? url = null, CancellationToken cancellationToken = default)
#pragma warning restore CA1054
    {
        string subject = configuration["WebPush:Subject"] ?? "mailto:contact@ridelance.ro";
        string? publicKey = configuration["WebPush:PublicKey"];
        string? privateKey = configuration["WebPush:PrivateKey"];

        if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey))
        {
            logger.LogWarning("WebPush keys are not configured. Push notification will not be sent.");
            return;
        }

        var vapidDetails = new VapidDetails(subject, publicKey, privateKey);
        using var webPushClient = new WebPushClient();

        var pushSubscription = new WebPush.PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);

        var payload = new
        {
            title,
            body,
            url = url ?? "/"
        };

        var serializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        string serializedPayload = JsonConvert.SerializeObject(payload, serializerSettings);

        try
        {
            await webPushClient.SendNotificationAsync(pushSubscription, serializedPayload, vapidDetails, cancellationToken);
            logger.LogInformation("Push notification sent to {UserId}", subscription.UserId);
        }
        catch (WebPushException ex)
        {
            logger.LogError(ex, "Failed to send push notification to {UserId}. StatusCode: {StatusCode}", subscription.UserId, ex.StatusCode);
            // Consider removing the subscription if it's expired or invalid (StatusCode == 410 or 404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while sending push notification to {UserId}", subscription.UserId);
        }
    }
}
