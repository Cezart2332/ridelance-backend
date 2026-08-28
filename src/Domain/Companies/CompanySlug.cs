using System.Globalization;

using Domain.Cars;

namespace Domain.Companies;

/// <summary>
/// Identitatea publică a unei firme: <c>tuki-go</c>, folosită în <c>/{slug}</c>.
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

    /// <summary>
    /// Primele segmente de cale pe care le folosește deja site-ul.
    /// </summary>
    /// <remarks>
    /// Mini-site-ul stă la rădăcină, <c>/{slug}</c>, deci un slug se bate cap în cap cu o pagină a
    /// site-ului dacă e același cuvânt: o firmă numită „Parteneri SRL" ar fi luat <c>/parteneri</c>
    /// și ar fi ascuns pagina de parteneri pentru toată lumea.
    ///
    /// Lista e ținută de mână, în oglindă cu tabelul de rute din <c>AppLayout.tsx</c> și cu rutele
    /// de prim nivel din <c>App.tsx</c>. Ruta de mini-site e ultima în tabel, deci un cuvânt de
    /// aici n-ar ajunge oricum la firmă — verificarea există ca firma să nu primească de la bun
    /// început un slug care n-o va deschide niciodată.
    /// </remarks>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        // Paginile publice
        "masini", "f", "intrebari-frecvente", "servicii", "despre-ridelance", "fiscal",
        "calculator-taxe", "abonamente-preturi", "parteneri", "contact", "programare",
        "dashboard", "dashboard-demo", "termeni-si-conditii", "privacy-policy",
        "politica-cookies", "politica-plati-abonamente", "semneaza",

        // Zonele cu cont
        "auth", "login", "inregistrare", "checkout", "app", "onboarding", "contabil",
        "admin", "poster", "demo",

        // Nume pe care le-ar cere orice pagină adăugată mâine
        "api", "assets", "static", "blog", "preturi", "firme", "ajutor", "cont", "setari",
    };

    /// <summary>Slug-ul ar acoperi o pagină a site-ului, deci nu poate fi al unei firme.</summary>
    public static bool IsReserved(string slug) => Reserved.Contains(slug);

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
