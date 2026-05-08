using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Chat;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Chat.GetAccountantRoom;

internal sealed class GetAccountantRoomCommandHandler(IApplicationDbContext context)
    : ICommandHandler<GetAccountantRoomCommand, GetAccountantRoomResponse>
{
    public async Task<Result<GetAccountantRoomResponse>> Handle(
        GetAccountantRoomCommand command,
        CancellationToken cancellationToken)
    {
        // Step 1: Find the assigned contabil for this client
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(p => p.AssignedContabil)
            .FirstOrDefaultAsync(p => p.UserId == command.ClientUserId, cancellationToken);

        if (registration?.AssignedContabilId is null || registration.AssignedContabil is null)
        {
            return Result.Failure<GetAccountantRoomResponse>(ChatErrors.NoSupportAgentAvailable);
        }

        // Step 2: Does this client already have an accountant chat room?
        ChatRoom? exactRoom = await context.ChatRooms
            .FirstOrDefaultAsync(r => r.ClientUserId == command.ClientUserId, cancellationToken);

        if (exactRoom is not null)
        {
            string name = $"{registration.AssignedContabil.FirstName} {registration.AssignedContabil.LastName}";
            return new GetAccountantRoomResponse(exactRoom.Id, registration.AssignedContabilId.Value, name);
        }

        var newRoom = new ChatRoom
        {
            Id = Guid.NewGuid(),
            ClientUserId = command.ClientUserId,
            ProfessionalUserId = registration.AssignedContabilId.Value,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.ChatRooms.Add(newRoom);
        await context.SaveChangesAsync(cancellationToken);

        string supportName = $"{registration.AssignedContabil.FirstName} {registration.AssignedContabil.LastName}";
        return new GetAccountantRoomResponse(newRoom.Id, registration.AssignedContabilId.Value, supportName);
    }
}
