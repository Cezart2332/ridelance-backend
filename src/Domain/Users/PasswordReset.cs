namespace Domain.Users;

/// <summary>
/// Regulile codului de resetare a parolei, trimis pe email. Se păstrează doar hash-ul lui: un cod
/// în clar în baza de date ar fi o cheie a contului pentru oricine o citește.
/// </summary>
public static class PasswordReset
{
    /// <summary>Șase cifre, ca la confirmarea emailului.</summary>
    public const int CodeLength = 6;

    /// <summary>Mai scurt decât codul de confirmare: acesta schimbă parola contului.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Câte coduri greșite se acceptă înainte ca cel curent să fie invalidat.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Pauza minimă între două trimiteri.</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    public const int MinPasswordLength = 8;
}
