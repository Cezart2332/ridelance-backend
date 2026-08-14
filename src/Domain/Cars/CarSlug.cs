using System.Globalization;
using System.Text;

namespace Domain.Cars;

/// <summary>
/// Identitatea publică a unui anunț: <c>dacia-logan-2022-4f3a</c>.
///
/// Slug-ul se generează din marcă, model și an — datele pe care le caută cineva când vede linkul.
/// Sufixul de patru caractere din Id există dintr-un singur motiv: două anunțuri pentru aceeași
/// mașină sunt normale (doi parteneri, aceeași Dacia Logan 2022), iar fără el al doilea n-ar mai
/// putea fi salvat. Nu e o măsură de securitate — Id-ul complet e oricum public.
/// </summary>
public static class CarSlug
{
    /// <summary>Cât din slug rămâne pentru text, ca totul să încapă în coloana de 160.</summary>
    private const int MaxTextLength = 140;

    public static string Generate(string brand, string model, int year, Guid id)
    {
        string text = Slugify($"{brand} {model} {year}");
        string suffix = id.ToString("N", CultureInfo.InvariantCulture)[..4];

        return text.Length == 0 ? suffix : $"{text}-{suffix}";
    }

    /// <summary>
    /// Text liber → kebab-case fără diacritice. „Škoda Octavia (facelift)” → „skoda-octavia-facelift”.
    /// </summary>
    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Descompunerea separă litera de accent, iar filtrul de mai jos aruncă accentul: „ă” → „a”.
        string normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                // Orice altceva (spațiu, punct, paranteză) e o singură cratimă, nu un șir de ele.
                builder.Append('-');
            }
        }

        string slug = builder.ToString().TrimEnd('-');
        return slug.Length > MaxTextLength ? slug[..MaxTextLength].TrimEnd('-') : slug;
    }
}
