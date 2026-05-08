using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Chat;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Chat.GetSupportRoom;

internal sealed class GetSupportRoomCommandHandler(IApplicationDbContext context)
    : ICommandHandler<GetSupportRoomCommand, GetSupportRoomResponse>
{
    public async Task<Result<GetSupportRoomResponse>> Handle(
        GetSupportRoomCommand command,
        CancellationToken cancellationToken)
    {
        User? adminUser = await context.Users
            .Where(u => u.Role == UserRole.Admin)
            .FirstOrDefaultAsync(cancellationToken);

        if (adminUser is null)
        {
            return Result.Failure<GetSupportRoomResponse>(ChatErrors.NoSupportAgentAvailable);
        }

        // Step 1: Does this client already have a chat room? Reuse it.
        ChatRoom? existingRoom = await context.ChatRooms
            .FirstOrDefaultAsync(r => r.ClientUserId == command.ClientUserId, cancellationToken);

        string adminName = $"{adminUser.FirstName} {adminUser.LastName}";

        if (existingRoom is not null)
        {
            return new GetSupportRoomResponse(existingRoom.Id, adminUser.Id, adminName);
        }

        var newRoom = new ChatRoom
        {
            Id = Guid.NewGuid(),
            ClientUserId = command.ClientUserId,
            ProfessionalUserId = adminUser.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.ChatRooms.Add(newRoom);
        await context.SaveChangesAsync(cancellationToken);

        return new GetSupportRoomResponse(newRoom.Id, adminUser.Id, adminName);
    }
}
