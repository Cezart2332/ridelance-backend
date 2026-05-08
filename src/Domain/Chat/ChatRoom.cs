using Domain.Users;
using SharedKernel;

namespace Domain.Chat;

public sealed class ChatRoom : Entity
{
    public Guid Id { get; set; }
    public Guid ClientUserId { get; set; }
    public Guid ProfessionalUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public User ClientUser { get; set; } = null!;
    public User ProfessionalUser { get; set; } = null!;
    public List<ChatMessage> Messages { get; set; } = [];
}
