using Application.Abstractions.Messaging;

namespace Application.Chat.GetMessages;

public sealed record GetMessagesQuery(
    Guid ChatRoomId,
    Guid RequestingUserId,
    int Page = 1,
    int PageSize = 50) : IQuery<ChatMessageListResponse>;

public sealed record ChatMessageListResponse(
    List<ChatMessageDto> Messages,
    int TotalCount);

public sealed record ChatMessageDto(
    Guid Id,
    Guid SenderId,
    string SenderName,
    string SenderRole,
    string Content,
    DateTime SentAtUtc,
    bool IsRead);
