using Application.Abstractions.Messaging;

namespace Application.Chat.GetAccountantRoom;

public sealed record GetAccountantRoomCommand(Guid ClientUserId) : ICommand<GetAccountantRoomResponse>;

public sealed record GetAccountantRoomResponse(Guid RoomId, Guid AccountantUserId, string AccountantUserName);
