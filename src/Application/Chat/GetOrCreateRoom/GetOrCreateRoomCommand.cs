using Application.Abstractions.Messaging;

namespace Application.Chat.GetOrCreateRoom;

public sealed record GetOrCreateRoomCommand(Guid ClientUserId, Guid ProfessionalUserId)
    : ICommand<Guid>;
