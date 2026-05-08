using SharedKernel;

namespace Domain.Chat;

public static class ChatErrors
{
    public static Error RoomNotFound(Guid roomId) => Error.NotFound(
        "Chat.RoomNotFound",
        $"The chat room with Id = '{roomId}' was not found.");

    public static readonly Error AccessDenied = Error.Failure(
        "Chat.AccessDenied",
        "You do not have permission to access this chat room.");

    public static readonly Error CannotChatWithSelf = Error.Failure(
        "Chat.CannotChatWithSelf",
        "You cannot create a chat room with yourself.");

    public static readonly Error NoSupportAgentAvailable = Error.NotFound(
        "Chat.NoSupportAgentAvailable",
        "No support agent (Contabil or Admin) is currently available.");
}
