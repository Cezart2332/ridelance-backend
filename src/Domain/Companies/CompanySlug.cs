using System.Globalization;

using Domain.Cars;

namespace Domain.Companies;

/// <summary>
/// Identitatea publică a unei firme: <c>tuki-go</c>, folosită în <c>/f/{slug}</c>.
/// </summary>
/// <remarks>
/// Se generează o singură dată, din denumire, și **nu** se regenerează la redenumire (spec §4.2):
/// un slug care se mută rupe fiecare link pe care proprietarul l-a distribuit deja.
///
/// Sufixul din Id apare doar la coliziune. Spre deosebire de anunțuri, unde două mașini identice
/// sunt normale, două firme cu aceeași denumire sunt excepția — iar <c>tuki-go</c> citit de un om
/// e mai bun decât <c>tuki-go-4f3a</c> pentru toată lumea care nu se lovește de coliziune.
/// </remarks>
public static class CompanySlug
{
    /// <summary>Cât din slug rămâne pentru text, ca sufixul de dezambiguizare să încapă în 160.</summary>
    private const int MaxTextLength = 150;

    /// <summary>Slug-ul preferat, fără garanție de unicitate. Apelantul verifică în baza de date.</summary>
    public static string Generate(string legalName)
    {
        string text = CarSlug.Slugify(legalName);
        if (text.Length > MaxTextLength)
        {
            text = text[..MaxTextLength].TrimEnd('-');
        }

        return text.Length == 0 ? "firma" : text;
    }

    /// <summary>Varianta dezambiguizată, când slug-ul preferat e deja luat.</summary>
    public static string Disambiguate(string preferred, Guid id)
    {
        string suffix = id.ToString("N", CultureInfo.InvariantCulture)[..4];
        return $"{preferred}-{suffix}";
    }
}
