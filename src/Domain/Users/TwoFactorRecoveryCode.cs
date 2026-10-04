namespace Domain.Users;

/// <summary>Un cod de recuperare 2FA, de unică folosință; se păstrează doar hash-ul.</summary>
public sealed class TwoFactorRecoveryCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAtUtc { get; set; }
}
