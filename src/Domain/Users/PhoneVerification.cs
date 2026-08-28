namespace Domain.Users;

/// <summary>
/// Regulile codului de confirmare a numărului de telefon.
/// </summary>
/// <remarks>
/// <para>
/// Aceleași reguli ca la email (<see cref="EmailVerification" />), cu două diferențe care vin din
/// mediu: codul trăiește mai puțin, fiindcă un SMS ajunge în câteva secunde și se citește pe loc,
/// nu peste o oră dintr-un inbox; iar pauza dintre trimiteri e mai lungă, fiindcă fiecare SMS e
/// plătit, deci un buton de retrimitere apăsat în draci costă bani, nu doar lățime de bandă.
/// </para>
/// <para>
/// Ca și confirmarea emailului, <b>nu e impusă</b> nicăieri: niciun endpoint nu cere
/// <see cref="User.IsPhoneVerified" />. Se afișează, atât.
/// </para>
/// </remarks>
public static class PhoneVerification
{
    /// <summary>Șase cifre, ca la email: se citesc din notificare și se tastează pe loc.</summary>
    public const int CodeLength = 6;

    /// <summary>Cât e valabil codul. Un SMS care întârzie zece minute n-a mai ajuns.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Câte coduri greșite se acceptă înainte ca cel curent să fie invalidat.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Pauza minimă între două trimiteri. Fiecare SMS costă.</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(90);
}
