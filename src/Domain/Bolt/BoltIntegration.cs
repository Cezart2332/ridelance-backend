using SharedKernel;
using Domain.Users;

namespace Domain.Bolt;

public sealed class BoltIntegration : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public string? AccessToken { get; set; }
    public DateTime? TokenExpiresAtUtc { get; set; }
    public DateTime? LastFetchedAtUtc { get; set; }
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
