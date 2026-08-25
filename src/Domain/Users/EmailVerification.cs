namespace Domain.Users;

/// <summary>
/// Regulile codului de confirmare a emailului.
/// </summary>
/// <remarks>
/// <para>
/// Confirmarea e trimisă la înregistrare și poate fi făcută, dar <b>nu e impusă</b>: nici
/// autentificarea, nici vreun endpoint nu verifică <see cref="User.IsEmailVerified" />. Un cont
/// neconfirmat funcționează complet.
/// </para>
/// <para>
/// Ca să devină obligatorie, sunt trei locuri de atins: <c>LoginUserCommandHandler</c>, care ar
/// trebui să refuze un cont neconfirmat; poarta din frontend, care ar trebui să nu lase
/// înregistrarea să treacă mai departe fără confirmare; și conturile existente, care sunt toate
/// neconfirmate și ar rămâne pe dinafară fără un backfill sau o dată de la care regula se aplică.
/// </para>
/// </remarks>
public static class EmailVerification
{
    /// <summary>Șase cifre — se citește dintr-un email și se tastează pe telefon.</summary>
    public const int CodeLength = 6;

    /// <summary>
    /// Cât timp e valabil codul. Scurt cât să nu stea în inbox la nesfârșit, lung cât să prindă
    /// o întârziere de livrare.
    /// </summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Câte coduri greșite se acceptă înainte ca cel curent să fie invalidat.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Pauza minimă între două trimiteri, ca butonul de retrimitere să nu fie o țeavă.</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
}
