using Domain.Users;
using SharedKernel;

namespace Domain.Notifications;

public sealed class Notification : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = "info";
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}
