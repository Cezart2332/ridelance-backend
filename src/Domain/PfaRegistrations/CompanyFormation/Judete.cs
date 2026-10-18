using System.Globalization;
using System.Text;

namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>
/// Cele 41 de județe plus București, cu indicativul auto. Aceeași listă ca în frontend
/// (<c>src/data/counties.ts</c>): formularul are un select care afișează gol orice valoare
/// care nu e exact una de aici.
/// </summary>
public static class Judete
{
    private static readonly (string Code, string Name)[] All =
    [
        ("AB", "Alba"), ("AR", "Arad"), ("AG", "Argeș"), ("BC", "Bacău"), ("BH", "Bihor"),
        ("BN", "Bistrița-Năsăud"), ("BT", "Botoșani"), ("BV", "Brașov"), ("BR", "Brăila"),
        ("B", "București"), ("BZ", "Buzău"), ("CS", "Caraș-Severin"), ("CL", "Călărași"),
        ("CJ", "Cluj"), ("CT", "Constanța"), ("CV", "Covasna"), ("DB", "Dâmbovița"), ("DJ", "Dolj"),
        ("GL", "Galați"), ("GR", "Giurgiu"), ("GJ", "Gorj"), ("HR", "Harghita"), ("HD", "Hunedoara"),
        ("IL", "Ialomița"), ("IS", "Iași"), ("IF", "Ilfov"), ("MM", "Maramureș"), ("MH", "Mehedinți"),
        ("MS", "Mureș"), ("NT", "Neamț"), ("OT", "Olt"), ("PH", "Prahova"), ("SM", "Satu Mare"),
        ("SJ", "Sălaj"), ("SB", "Sibiu"), ("SV", "Suceava"), ("TR", "Teleorman"), ("TM", "Timiș"),
        ("TL", "Tulcea"), ("VS", "Vaslui"), ("VL", "Vâlcea"), ("VN", "Vrancea"),
    ];

    /// <summary>
    /// Numele din listă pentru ce scrie pe buletin: „CJ", „Jud.CJ", „CLUJ", „Bistrita-Nasaud",
    /// „Mun. București", „Sector 3". Null dacă nu se recunoaște.
    /// </summary>
    public static string? Canonical(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string folded = Fold(raw);
        foreach (string prefix in new[] { "judetul", "jud", "municipiul", "mun" })
        {
            if (folded.StartsWith(prefix, StringComparison.Ordinal) && folded.Length > prefix.Length)
            {
                folded = folded[prefix.Length..];
                break;
            }
        }

        // Buletinele din București scriu sectorul în loc de județ.
        if (folded.StartsWith("sector", StringComparison.Ordinal) || folded.StartsWith("sec", StringComparison.Ordinal) && folded.Length <= 4)
        {
            return "București";
        }

        foreach ((string code, string name) in All)
        {
            if (folded == Fold(name) || string.Equals(folded, code, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>Indicativul auto al unui județ, oricum ar fi scris. Null dacă nu se recunoaște.</summary>
    public static string? Code(string? raw)
    {
        string? name = Canonical(raw);
        return name is null ? null : All.First(j => j.Name == name).Code;
    }

    /// <summary>Fără diacritice, spații, puncte și cratime, cu litere mici.</summary>
    public static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char c in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
