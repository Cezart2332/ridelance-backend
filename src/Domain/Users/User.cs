using SharedKernel;

namespace Domain.Users;

public sealed class User : Entity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public UserRole Role { get; set; } = UserRole.Client;
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryUtc { get; set; }
    public DateTime? LastActivityAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Când a fost confirmat emailul. <see langword="null" /> înseamnă neconfirmat.
    /// </summary>
    /// <remarks>
    /// Se scrie, dar încă nu se citește nicăieri ca o condiție: confirmarea e trimisă și
    /// acceptată, dar nu blochează autentificarea sau accesul. Vezi
    /// <see cref="EmailVerification" /> pentru ce lipsește ca să devină obligatorie.
    /// </remarks>
    public DateTime? EmailVerifiedAtUtc { get; set; }

    /// <summary>Codul din email. Se șterge la confirmare, ca să nu poată fi refolosit.</summary>
    public string? EmailVerificationCode { get; set; }

    public DateTime? EmailVerificationCodeExpiresAtUtc { get; set; }

    /// <summary>
    /// Câte coduri greșite s-au încercat de la ultima trimitere. Un cod de șase cifre are un
    /// milion de variante, ceea ce e puțin fără o limită de încercări.
    /// </summary>
    public int EmailVerificationAttempts { get; set; }

    public bool IsEmailVerified => EmailVerifiedAtUtc.HasValue;

    public List<PushSubscription> PushSubscriptions { get; set; } = [];
}
