using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Chat;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Chat.GetOrCreateRoom;

internal sealed class GetOrCreateRoomCommandHandler(IApplicationDbContext context)
    : ICommandHandler<GetOrCreateRoomCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        GetOrCreateRoomCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ClientUserId == command.ProfessionalUserId)
        {
            return Result.Failure<Guid>(ChatErrors.CannotChatWithSelf);
        }

        ChatRoom? existingRoom = await context.ChatRooms
            .FirstOrDefaultAsync(r => r.ClientUserId == command.ClientUserId, cancellationToken);

        if (existingRoom is not null)
        {
            return existingRoom.Id;
        }

        var room = new ChatRoom
        {
            Id = Guid.NewGuid(),
            ClientUserId = command.ClientUserId,
            ProfessionalUserId = command.ProfessionalUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.ChatRooms.Add(room);
        await context.SaveChangesAsync(cancellationToken);

        return room.Id;
    }
}
