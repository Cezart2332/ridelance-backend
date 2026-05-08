using Application.Abstractions.Messaging;

namespace Application.Chat.SendMessage;

public sealed record SendMessageCommand(
    Guid ChatRoomId,
    Guid SenderId,
    string Content) : ICommand<Guid>;
