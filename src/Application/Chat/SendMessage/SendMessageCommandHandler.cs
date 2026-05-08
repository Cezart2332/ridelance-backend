using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Chat;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Chat.SendMessage;

internal sealed class SendMessageCommandHandler(IApplicationDbContext context)
    : ICommandHandler<SendMessageCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        SendMessageCommand command,
        CancellationToken cancellationToken)
    {
        ChatRoom? room = await context.ChatRooms
            .SingleOrDefaultAsync(r => r.Id == command.ChatRoomId, cancellationToken);

        if (room is null)
        {
            return Result.Failure<Guid>(ChatErrors.RoomNotFound(command.ChatRoomId));
        }

        User? user = await context.Users
            .SingleOrDefaultAsync(u => u.Id == command.SenderId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<Guid>(ChatErrors.AccessDenied);
        }

        bool isParticipant = false;

        if (user.Role == UserRole.Admin)
        {
            isParticipant = true;
        }
        else if (user.Role == UserRole.Contabil)
        {
            Domain.PfaRegistrations.PfaRegistration? pfa = await context.PfaRegistrations
                .SingleOrDefaultAsync(p => p.UserId == room.ClientUserId, cancellationToken);

            if (pfa?.AssignedContabilId == command.SenderId || room.ProfessionalUserId == command.SenderId)
            {
                isParticipant = true;
            }
        }
        else
        {
            if (room.ClientUserId == command.SenderId)
            {
                isParticipant = true;
            }
        }

        if (!isParticipant)
        {
            return Result.Failure<Guid>(ChatErrors.AccessDenied);
        }

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            ChatRoomId = command.ChatRoomId,
            SenderId = command.SenderId,
            Content = command.Content,
            SentAtUtc = DateTime.UtcNow,
            IsRead = false
        };

        room.LastMessageAtUtc = message.SentAtUtc;
        context.ChatMessages.Add(message);
        await context.SaveChangesAsync(cancellationToken);

        return message.Id;
    }
}
