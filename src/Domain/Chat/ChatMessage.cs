using Domain.Users;
using SharedKernel;

namespace Domain.Chat;

public sealed class ChatMessage : Entity
{
    public Guid Id { get; set; }
    public Guid ChatRoomId { get; set; }
    public Guid SenderId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }

    // Navigation
    public ChatRoom ChatRoom { get; set; } = null!;
    public User Sender { get; set; } = null!;
}
