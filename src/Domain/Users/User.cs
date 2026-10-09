using SharedKernel;

namespace Domain.Users;

public sealed class User : Entity
{
    public bool FleetOnboardingRequired { get; set; }
    public Domain.Companies.FleetOnboarding FleetOnboarding { get; set; } = new();
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
    /// Când a fost închis contul. <see langword="null" /> = cont deschis.
    /// </summary>
    /// <remarks>
    /// Ștergerea e logică, nu fizică: rândul rămâne, cu tot ce ține de el — dosarul PFA,
    /// documentele, plățile, facturile, închirierile. Ne trebuie pentru contabilitate, pentru
    /// obligațiile legale de păstrare și ca adminul să vadă istoricul unui fost client. Ce se
    /// schimbă e accesul: autentificarea și reînnoirea sesiunii sunt refuzate.
    /// </remarks>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>Adminul care a închis contul.</summary>
    public Guid? DeletedByUserId { get; set; }

    /// <summary>Motivul notat de admin la închidere.</summary>
    public string? DeletionReason { get; set; }

    public bool IsDeleted => DeletedAtUtc is not null;

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

    /// <summary>
    /// Când a fost confirmat numărul de telefon. <see langword="null" /> înseamnă neconfirmat.
    /// </summary>
    /// <remarks>
    /// Confirmarea se face pe numărul din <see cref="PhoneNumber" />, iar schimbarea numărului o
    /// anulează — altfel un număr confirmat o dată ar rămâne „confirmat" după ce a fost înlocuit
    /// cu altul, ceea ce e exact pe dos față de ce garantează bifa.
    /// </remarks>
    public DateTime? PhoneVerifiedAtUtc { get; set; }

    /// <summary>
    /// Până când e valabil codul trimis prin SMS. Codul în sine îl ține Twilio Verify; noi păstrăm
    /// fereastra, pentru pauza dintre retrimiteri și mesajul „a expirat”.
    /// </summary>
    public DateTime? PhoneVerificationCodeExpiresAtUtc { get; set; }

    /// <summary>Câte coduri greșite s-au încercat de la ultima trimitere.</summary>
    public int PhoneVerificationAttempts { get; set; }

    public bool IsPhoneVerified => PhoneVerifiedAtUtc.HasValue;

    public List<PushSubscription> PushSubscriptions { get; set; } = [];

    /// <summary>
    /// Primul admin al platformei (creat din <c>Bootstrap:AdminEmail</c>). Doar el poate invita alți
    /// admini și le poate reseta 2FA-ul.
    /// </summary>
    public bool IsOwner { get; set; }

    /// <summary>Secretul TOTP confirmat, criptat (<c>ISecretProtector</c>). <c>null</c> = fără 2FA.</summary>
    public string? TwoFactorSecret { get; set; }

    /// <summary>Secretul generat la configurare, încă neconfirmat cu un cod.</summary>
    public string? TwoFactorPendingSecret { get; set; }

    public DateTime? TwoFactorEnabledAtUtc { get; set; }

    /// <summary>Ultimul pas de 30 s acceptat: un cod deja folosit nu mai trece a doua oară.</summary>
    public long? TwoFactorLastStep { get; set; }

    public int TwoFactorFailedAttempts { get; set; }

    public DateTime? TwoFactorLockedUntilUtc { get; set; }

    /// <summary>
    /// Hash-ul tokenului de după parolă (verificare sau configurare 2FA): scurt, de unică folosință,
    /// fără drept de acces la API.
    /// </summary>
    public string? TwoFactorChallengeHash { get; set; }

    public DateTime? TwoFactorChallengeExpiresAtUtc { get; set; }

    public bool IsTwoFactorEnabled => TwoFactorSecret is not null;

    /// <summary>Echipa RIDElance: pentru ei 2FA e obligatoriu.</summary>
    public bool IsStaff => Role is UserRole.Admin or UserRole.Contabil;
}
