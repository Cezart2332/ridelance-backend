namespace Domain.Users;

/// <summary>
/// Invitația unui membru al echipei (Admin sau Contabil): un link de unică folosință, cu termen.
/// Contul se creează abia la acceptare, cu parola aleasă de om și 2FA configurat pe loc.
/// </summary>
public sealed class StaffInvitation
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    /// <summary>SHA-256 al tokenului din link; tokenul în clar nu se păstrează.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Invitația primului admin, creată la pornire: contul rezultat e proprietarul platformei.</summary>
    public bool IsBootstrap { get; set; }

    /// <summary><c>null</c> pentru invitația de bootstrap.</summary>
    public Guid? InvitedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAtUtc { get; set; }
    public Guid? AcceptedUserId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public bool IsOpen(DateTime nowUtc) => AcceptedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}
