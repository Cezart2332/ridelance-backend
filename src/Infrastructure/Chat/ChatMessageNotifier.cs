using Application.Abstractions.Data;
using Application.Abstractions.Notifications;
using Application.Chat.GetMessages;
using Domain.Chat;
using Domain.Notifications;
using Domain.Users;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Chat;

/// <summary>
/// Ce se întâmplă după ce un mesaj s-a salvat, indiferent dacă a venit prin hub (text) sau prin
/// upload (fișier): ajunge live în cameră, iar celălalt participant primește notificare și push.
/// </summary>
public sealed class ChatMessageNotifier(
    IApplicationDbContext context,
    IHubContext<ChatHub> hub,
    IWebPushService webPushService,
    IConfiguration configuration)
{
    public async Task PublishAsync(Guid roomId, ChatMessageDto message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await hub.Clients.Group(roomId.ToString()).SendAsync("ReceiveMessage", message, cancellationToken);

        ChatRoom? room = await context.ChatRooms.AsNoTracking().SingleOrDefaultAsync(r => r.Id == roomId, cancellationToken);
        User? sender = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == message.SenderId, cancellationToken);
        if (room is null || sender is null)
        {
            return;
        }

        Guid recipientId = sender.Role == UserRole.Client ? room.ProfessionalUserId : room.ClientUserId;

        string senderRoleLabel = sender.Role switch
        {
            UserRole.Client => "Client",
            UserRole.Contabil => "Contabil",
            UserRole.Admin => "Admin",
            _ => "Utilizator"
        };

        string preview = message.Content;
        if (message.Attachment is not null)
        {
            preview = string.IsNullOrWhiteSpace(preview)
                ? $"a trimis un fișier: {message.Attachment.FileName}"
                : $"{preview} (fișier atașat)";
        }

        string truncatedContent = preview.Length > 50 ? preview[..50] + "..." : preview;

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = recipientId,
            Text = $"Mesaj de la {senderRoleLabel}: {truncatedContent}",
            Type = NotificationTypes.ChatRoomMessage,
            RelatedUserId = room.ClientUserId,
            SectionKey = senderRoleLabel,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.Notifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken);

        List<PushSubscription> subscriptions = await context.PushSubscriptions
            .Where(s => s.UserId == recipientId)
            .ToListAsync(cancellationToken);

        string shortContent = preview.Length > 25 ? preview[..25] + "..." : preview;
        string pushBody = $"{senderRoleLabel}: {shortContent}";

        Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
        string relativePath = $"/app/notificari/{notification.Id}";
        string deepLink = appBaseUri is null ? relativePath : new Uri(appBaseUri, relativePath).ToString();

        foreach (PushSubscription sub in subscriptions)
        {
            try
            {
                await webPushService.SendPushNotificationAsync(sub, "Mesaj nou", pushBody, deepLink, cancellationToken);
            }
            catch
            {
                // Ignore push errors to not interrupt the chat flow
            }
        }
    }
}
