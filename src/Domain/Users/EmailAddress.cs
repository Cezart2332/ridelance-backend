namespace Domain.Users;

/// <summary>
/// Forma în care se scrie și se caută o adresă de email: fără spații la capete, cu litere mici.
/// „Ion@Gmail.com” și „ion@gmail.com” sunt aceeași căsuță, deci trebuie să fie același cont.
/// </summary>
public static class EmailAddress
{
    // Litere mici, nu mari (CA1308): adresele se afișează și se trimit așa cum le scriu oamenii,
    // iar forma uzuală e cu litere mici.
#pragma warning disable CA1308
    public static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
#pragma warning restore CA1308
}
