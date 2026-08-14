namespace Domain.Users;

/// <summary>
/// Numele de afișat al unui cont, cu un singur fallback pentru toată aplicația.
///
/// De la RL-05 contul se creează doar cu email și parolă — numele vine mai târziu, din buletin.
/// Asta înseamnă că orice loc care concatena naiv <c>FirstName + " " + LastName</c> ar afișa un
/// spațiu gol sau „Salut, ” într-un email. Regula trăiește aici, o singură dată, ca toate
/// suprafețele (avatar, header, emailuri, admin) să spună același lucru.
/// </summary>
public static class UserDisplayName
{
    private const string Fallback = "Contul meu";

    /// <summary>Numele complet, dacă există. Altfel partea locală a emailului, altfel „Contul meu”.</summary>
    public static string Of(User user) => Of(user.FirstName, user.LastName, user.Email);

    public static string Of(string? firstName, string? lastName, string? email)
    {
        string full = $"{firstName} {lastName}".Trim();

        if (!string.IsNullOrWhiteSpace(full))
        {
            return full;
        }

        string? local = LocalPartOf(email);
        return string.IsNullOrWhiteSpace(local) ? Fallback : local;
    }

    /// <summary>
    /// Formula de adresare din emailuri. Aceeași sursă ca <see cref="Of(User)"/>, ca să nu apară
    /// „Salut, null”.
    /// </summary>
    public static string GreetingFor(User user)
    {
        string? firstName = user.FirstName?.Trim();
        return string.IsNullOrWhiteSpace(firstName) ? Of(user) : firstName;
    }

    /// <summary>Numele are voie să lipsească — cu excepția actelor oficiale, unde nu.</summary>
    public static bool IsMissing(User user) =>
        string.IsNullOrWhiteSpace($"{user.FirstName} {user.LastName}".Trim());

    private static string? LocalPartOf(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        int at = email.IndexOf('@', StringComparison.Ordinal);
        return at > 0 ? email[..at] : email;
    }
}
