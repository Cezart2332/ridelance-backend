using System.Security.Claims;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Chat.SendMessage;
using Domain.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Infrastructure.Chat;

[Authorize]
public sealed class ChatHub(
    IApplicationDbContext context,
    ICommandHandler<SendMessageCommand, Guid> sendMessageHandler) : Hub
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
