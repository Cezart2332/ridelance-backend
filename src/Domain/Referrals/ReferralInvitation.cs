namespace Domain.Referrals;

/// <summary>
/// Invitația trimisă de un client unui prieten, pe email, cu codul lui de recomandare. Statusul nu
/// se ține aici: se citește din contul creat cu adresa asta (onboarding, abonament), ca să nu
/// rămână în urmă.
/// </summary>
public sealed class ReferralInvitation
{
    public Guid Id { get; set; }

    /// <summary>Clientul care a trimis invitația.</summary>
    public Guid InviterUserId { get; set; }

    /// <summary>Adresa invitată, normalizată (trim + litere mici).</summary>
    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Ultima trimitere: o invitație repetată către aceeași adresă se retrimite, nu se dublează.</summary>
    public DateTime LastSentAtUtc { get; set; } = DateTime.UtcNow;
}
