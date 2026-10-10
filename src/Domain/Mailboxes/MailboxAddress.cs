using System.Globalization;
using System.Text;

namespace Domain.Mailboxes;

/// <summary>
/// Adresa operațională, din numele titularului: <c>prenume.nume</c>, cu primul prenume și numele de
/// familie. „Ștefan-Andrei Țăranu” → <c>stefan.taranu</c>; identitatea RIDElance e
/// <c>rid-ops-stefan.taranu</c>.
/// </summary>
public static class MailboxAddress
{
    public const string OpsIdentityPrefix = "rid-ops-";

    private static readonly char[] NameSeparators = [' ', '-', '\t', ' '];

    /// <summary>
    /// Partea locală de bază, sau <c>null</c> dacă din nume nu rămâne nimic folosibil (fără litere
    /// latine, de exemplu): atunci adresa nu se poate genera singură.
    /// </summary>
    public static string? BaseLocalPart(string? firstName, string? lastName)
    {
        // Primul prenume: „Ștefan-Andrei” și „Ștefan Andrei” dau amândouă „stefan”.
        string given = Clean(firstName).Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        // Numele de familie compus rămâne întreg, legat cu cratimă: „Popa Ionescu” → „popa-ionescu”.
        string family = Clean(lastName);

        if (given.Length == 0 || family.Length == 0)
        {
            return null;
        }

        return $"{given}.{family}";
    }

    /// <summary>A n-a încercare la coliziune: <c>ion.popescu</c>, <c>ion.popescu2</c>, <c>ion.popescu3</c>…</summary>
    public static string WithSuffix(string baseLocalPart, int attempt) =>
        attempt <= 1 ? baseLocalPart : baseLocalPart + attempt.ToString(CultureInfo.InvariantCulture);

    public static string OpsIdentityOf(string localPart) => OpsIdentityPrefix + localPart;

    public static string Compose(string localPart, string domain) => $"{localPart}@{domain}";

    /// <summary>Litere mici, fără diacritice, separatorii → <c>-</c>, doar <c>[a-z0-9-]</c>.</summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (char raw in value.Trim().Normalize(NormalizationForm.FormD))
        {
            // Semnele diacritice desprinse de FormD (virgula de sub ș/ț, sedila de sub ş/ţ, căciula lui ă).
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char c = char.ToLowerInvariant(raw);
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (Array.IndexOf(NameSeparators, c) >= 0 && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
