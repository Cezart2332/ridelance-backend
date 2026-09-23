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

    // Fișierul atașat (poză, PDF), criptat pe disc ca documentele. Fără atașament: toate null.
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentSize { get; set; }
    public string? AttachmentPath { get; set; }
    public string? AttachmentIv { get; set; }

    // Navigation
    public ChatRoom ChatRoom { get; set; } = null!;
    public User Sender { get; set; } = null!;
}
