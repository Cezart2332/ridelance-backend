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

    /// <summary>
    /// Cheia care împiedică trimiterea de două ori a aceleiași notificări.
    /// </summary>
    /// <remarks>
    /// Până acum eticheta asta stătea **în text**, între paranteze drepte, iar utilizatorul o citea
    /// odată cu mesajul: „Documentul tău expiră în 30 de zile. [expiry:8f3a…:30d:2026-08-28]".
    /// Entitatea n-avea unde altundeva s-o pună.
    ///
    /// `null` pentru notificările care nu se repetă și n-au ce dubla.
    /// </remarks>
    public string? DedupeKey { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}
