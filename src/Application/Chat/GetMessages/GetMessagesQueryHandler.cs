using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Chat;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Chat.GetMessages;

internal sealed class GetMessagesQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetMessagesQuery, ChatMessageListResponse>
{
    public async Task<Result<ChatMessageListResponse>> Handle(
        GetMessagesQuery query,
        CancellationToken cancellationToken)
    {
        ChatRoom? room = await context.ChatRooms
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == query.ChatRoomId, cancellationToken);

        if (room is null)
        {
            return Result.Failure<ChatMessageListResponse>(ChatErrors.RoomNotFound(query.ChatRoomId));
        }

        Domain.Users.User? user = await context.Users
            .SingleOrDefaultAsync(u => u.Id == query.RequestingUserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<ChatMessageListResponse>(ChatErrors.AccessDenied);
        }

        bool isParticipant = false;

        if (user.Role == Domain.Users.UserRole.Admin)
        {
            isParticipant = true;
        }
        else if (user.Role == Domain.Users.UserRole.Contabil)
        {
            Domain.PfaRegistrations.PfaRegistration? pfa = await context.PfaRegistrations
                .SingleOrDefaultAsync(p => p.UserId == room.ClientUserId, cancellationToken);

            if (pfa?.AssignedContabilId == query.RequestingUserId || room.ProfessionalUserId == query.RequestingUserId)
            {
                isParticipant = true;
            }
        }
        else
        {
            if (room.ClientUserId == query.RequestingUserId)
            {
                isParticipant = true;
            }
        }

        if (!isParticipant)
        {
            return Result.Failure<ChatMessageListResponse>(ChatErrors.AccessDenied);
        }

        IQueryable<ChatMessage> messagesQuery = context.ChatMessages
            .AsNoTracking()
            .Where(m => m.ChatRoomId == query.ChatRoomId);

        if (user.Role == Domain.Users.UserRole.Contabil)
        {
            messagesQuery = messagesQuery.Where(m => m.Sender.Role != Domain.Users.UserRole.Admin);
        }

        int totalCount = await messagesQuery.CountAsync(cancellationToken);

        List<ChatMessageDto> messages = await messagesQuery
            .Include(m => m.Sender)
            .OrderByDescending(m => m.SentAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(m => new ChatMessageDto(
                m.Id,
                m.SenderId,
                (m.Sender.Role == Domain.Users.UserRole.Admin || m.Sender.Role == Domain.Users.UserRole.Contabil) ? "Support Ridelance" : $"{m.Sender.FirstName} {m.Sender.LastName}",
                m.Sender.Role.ToString(),
                m.Content,
                m.SentAtUtc,
                m.IsRead))
            .ToListAsync(cancellationToken);

        // Reverse to show oldest first in page
        messages.Reverse();

        return new ChatMessageListResponse(messages, totalCount);
    }
}
