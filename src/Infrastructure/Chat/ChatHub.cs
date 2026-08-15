using System.Security.Claims;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Application.Chat.SendMessage;
using Domain.Chat;
using Domain.Notifications;
using Domain.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Chat;

[Authorize]
public sealed class ChatHub(
    IApplicationDbContext context,
    ICommandHandler<SendMessageCommand, Guid> sendMessageHandler,
    IWebPushService webPushService,
    IConfiguration configuration) : Hub
{
    public async Task JoinRoom(string roomId)
    {
        Guid userId = GetUserId();
        var roomGuid = Guid.Parse(roomId);

        ChatRoom room = await context.ChatRooms
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == roomGuid)
            ?? throw new HubException("Chat room not found.");

        Domain.Users.User user = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId)
            ?? throw new HubException("User not found.");

        bool isParticipant = false;

        if (user.Role == Domain.Users.UserRole.Admin)
        {
            isParticipant = true; // Admins can join any room
        }
        else if (user.Role == Domain.Users.UserRole.Contabil)
        {
            // Contabil can join if the PFA is assigned to them
            Domain.PfaRegistrations.PfaRegistration? pfa = await context.PfaRegistrations
                .AsNoTracking()
                .SingleOrDefaultAsync(p => p.UserId == room.ClientUserId);

            if (pfa?.AssignedContabilId == userId || room.ProfessionalUserId == userId)
            {
                isParticipant = true;
            }
        }
        else
        {
            // Client can join their own room
            if (room.ClientUserId == userId)
            {
                isParticipant = true;
            }
        }

        if (!isParticipant)
        {
            throw new HubException("Access denied.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
    }

    public async Task LeaveRoom(string roomId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
    }

    public async Task SendMessage(string roomId, string content)
    {
        Guid userId = GetUserId();
        var roomGuid = Guid.Parse(roomId);

        var command = new SendMessageCommand(roomGuid, userId, content);
        SharedKernel.Result<Guid> result = await sendMessageHandler.Handle(command, CancellationToken.None);

        if (result.IsFailure)
        {
            throw new HubException(result.Error.Description);
        }

        // Get sender name
        Domain.Users.User? sender = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId);

        string senderName = "Unknown";
        if (sender is not null)
        {
            senderName = (sender.Role == Domain.Users.UserRole.Admin || sender.Role == Domain.Users.UserRole.Contabil)
                ? "Support Ridelance"
                : $"{sender.FirstName} {sender.LastName}";
        }

        await Clients.Group(roomId).SendAsync("ReceiveMessage", new
        {
            id = result.Value,
            senderId = userId,
            senderName,
            senderRole = sender?.Role.ToString(),
            content,
            sentAtUtc = DateTime.UtcNow,
            isRead = false
        });

        // Send notifications
        ChatRoom? room = await context.ChatRooms
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == roomGuid);

        if (room is not null && sender is not null)
        {
            Guid recipientId = sender.Role == Domain.Users.UserRole.Client
                ? room.ProfessionalUserId
                : room.ClientUserId;

            string senderRoleLabel = sender.Role switch
            {
                Domain.Users.UserRole.Client => "Client",
                Domain.Users.UserRole.Contabil => "Contabil",
                Domain.Users.UserRole.Admin => "Admin",
                _ => "Utilizator"
            };

            string truncatedContent = content.Length > 50 ? content[..50] + "..." : content;
            string notificationText = $"Mesaj de la {senderRoleLabel}: {truncatedContent}";

            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = recipientId,
                Text = notificationText,
                Type = NotificationTypes.ChatRoomMessage,
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            };

            context.Notifications.Add(notification);
            await context.SaveChangesAsync();

            // Send push notification
            List<PushSubscription> subscriptions = await context.PushSubscriptions
                .Where(s => s.UserId == recipientId)
                .ToListAsync();

            string pushTitle = "Mesaj nou";
            string shortContent = content.Length > 25 ? content[..25] + "..." : content;
            string pushBody = $"{senderRoleLabel}: {shortContent}";

            // Generate deep link
            Domain.Users.User? recipient = await context.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.Id == recipientId);
            
            string? deepLink = null;
            if (recipient is not null)
            {
                Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
                string relativePath = recipient.Role switch
                {
                    Domain.Users.UserRole.Client => "/app/dashboard/suport",
                    Domain.Users.UserRole.Contabil => "/contabil/dashboard",
                    Domain.Users.UserRole.Admin => "/admin/dashboard",
                    _ => "/app/dashboard"
                };
                deepLink = appBaseUri is null ? relativePath : new Uri(appBaseUri, relativePath).ToString();
            }

            foreach (PushSubscription sub in subscriptions)
            {
                try
                {
                    await webPushService.SendPushNotificationAsync(sub, pushTitle, pushBody, deepLink);
                }
                catch
                {
                    // Ignore push errors to not interrupt the chat flow
                }
            }
        }
    }

    private Guid GetUserId()
    {
        string? userIdClaim = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out Guid userId))
        {
            throw new HubException("User not authenticated.");
        }

        return userId;
    }
}
