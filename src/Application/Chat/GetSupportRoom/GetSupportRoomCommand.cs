using Application.Abstractions.Messaging;

namespace Application.Chat.GetSupportRoom;

/// <summary>
/// Gets or creates a support chat room between a client and any available contabil/admin.
/// The system auto-selects the support agent with the fewest active rooms.
/// </summary>
public sealed record GetSupportRoomCommand(Guid ClientUserId) : ICommand<GetSupportRoomResponse>;

public sealed record GetSupportRoomResponse(Guid RoomId, Guid SupportUserId, string SupportUserName);
