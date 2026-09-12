using SharedKernel;
using Domain.Users;

namespace Domain.Banking;

public sealed class BankConnection : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// Providerul care deține conexiunea (ex. „SmartAccounts"). Rândurile rămase de la un provider
    /// anterior se recunosc după el — nu se convertesc, pentru că nu au echivalent.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Codul băncii la furnizor („BT", „BCR", …), parte din adresa oricărui apel.</summary>
    public string InstitutionId { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string? InstitutionLogoUrl { get; set; }

    /// <summary>
    /// IP-ul clientului, de la deschiderea consimțământului.
    ///
    /// Furnizorul cere antetul PSU-IP-Address la FIECARE apel pe consimțământ, nu doar la
    /// deschidere: fără el răspunde 400 „EMPTY OR MISSING PSU-IP-ADDRESS" și la citirea stării, și
    /// la conturi. Jobul de sincronizare n-are cerere HTTP din care să-l ia, deci îl păstrăm aici.
    /// </summary>
    public string? PsuIpAddress { get; set; }

    /// <summary>
    /// Identificatorul consimțământului la furnizor, în clar.
    ///
    /// În clar fiindcă după el se caută rândul de fiecare dată când furnizorul răspunde, iar
    /// cifrarea noastră folosește nonce aleator — același identificator ar da alt text la fiecare
    /// scriere, deci n-ar mai fi de căutat după el. Singur nu deschide nimic: apelurile cer și
    /// certificatul de client, și tokenurile de mai jos.
    /// </summary>
    public string ProviderConsentId { get; set; } = string.Empty;

    /// <summary>Tokenul de acces al consimțământului, criptat. Ține 5 minute.</summary>
    public string? AccessTokenEncrypted { get; set; }

    /// <summary>
    /// Tokenul de reîmprospătare, criptat. Ține 90 de zile și se rotește la fiecare folosire — cel
    /// emis ultima dată e singurul valid, deci se salvează imediat ce vine, nu la final.
    /// </summary>
    public string? RefreshTokenEncrypted { get; set; }

    public DateTime? AccessTokenExpiresAtUtc { get; set; }

    /// <summary>Ultima stare a consimțământului văzută la bancă („received", „valid", „expired", …).</summary>
    public string? ConsentStatus { get; set; }

    /// <summary>
    /// Referința noastră, pusă în adresa de retur trimisă furnizorului. Ea leagă omul care se
    /// întoarce din pagina băncii de conexiunea care îl aștepta.
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// Până când e valabilă adresa de autorizare a băncii. Trecută de ea fără ca utilizatorul să
    /// termine, conexiunea se ia de la capăt.
    /// </summary>
    public DateTime? LinkExpiresAtUtc { get; set; }

    public BankConnectionStatus Status { get; set; }
    public DateTime? ConsentExpiresAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? ExpiryNotifiedAtUtc { get; set; }

    public int MaxHistoricalDays { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LinkedAtUtc { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public List<BankAccount> Accounts { get; set; } = [];
}
