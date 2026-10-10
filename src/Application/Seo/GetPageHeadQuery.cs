using System.Globalization;
using System.Text;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
using Domain.Cars;
using Domain.Companies;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Seo;

/// <summary>
/// <c>GET /seo/head?path=…</c> — ce are în <c>&lt;head&gt;</c> o pagină publică al cărei conținut
/// vine din date: anunțul unei mașini sau pagina unei firme.
/// </summary>
/// <remarks>
/// Site-ul e randat în browser, deci serverul lui (nginx) nu știe nici titlul unei mașini, nici dacă
/// adresa cerută există. Întreabă aici, la fiecare pagină de acest fel, și folosește răspunsul de
/// două ori: starea spune dacă pagina există (altfel răspunde 404), iar corpul intră în
/// <c>&lt;head&gt;</c>-ul paginii trimise, ca titlul, descrierea și imaginea să ajungă și la cine nu
/// rulează JavaScript — previzualizarea unui link distribuit, de exemplu.
///
/// Trei răspunsuri:
/// <list type="bullet">
/// <item>etichetele paginii, când adresa e o mașină publică sau o firmă;</item>
/// <item><see cref="PageHead.None"/>, când adresa e a aplicației (o zonă de cont, o pagină statică):
/// există, dar nu are etichete proprii aici;</item>
/// <item><c>NotFound</c>, când la adresa aceea nu e nimic.</item>
/// </list>
///
/// Titlul și descrierea sunt aceleași pe care le pune aplicația după ce pornește
/// (<c>VehicleSeo.tsx</c>, <c>CompanyPublicPage.tsx</c>). Trebuie ținute identice: altfel pagina își
/// schimbă titlul sub ochii cititorului.
/// </remarks>
/// <param name="Path">Calea cerută, fără interogare: <c>/masini/dacia-logan-2021</c>.</param>
/// <param name="SiteOrigin">Adresa publică a site-ului.</param>
public sealed record GetPageHeadQuery(string Path, string SiteOrigin) : IQuery<PageHead>;

/// <param name="Html">Etichetele de pus în <c>&lt;head&gt;</c>; gol = pagina le păstrează pe cele implicite.</param>
public sealed record PageHead(string Html)
{
    public static readonly PageHead None = new(string.Empty);
}

internal sealed class GetPageHeadQueryHandler(IApplicationDbContext context) : IQueryHandler<GetPageHeadQuery, PageHead>
{
    private const string SiteName = "RIDElance";

    /// <summary>Cât dintr-o descriere ajunge într-un rezultat de căutare.</summary>
    private const int MaxDescriptionLength = 160;

    private static readonly Error Missing = Error.NotFound("Seo.PageNotFound", "Pagina nu există.");

    // Aceeași scriere a prețului ca în aplicație: „1.200”, fără zecimale.
    private static readonly NumberFormatInfo Lei = new() { NumberGroupSeparator = ".", NumberDecimalDigits = 0 };
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    public async Task<Result<PageHead>> Handle(GetPageHeadQuery query, CancellationToken cancellationToken)
    {
        string[] segments = query.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string site = query.SiteOrigin.TrimEnd('/');

        if (segments.Length == 0)
        {
            return PageHead.None;
        }

        // Zonele aplicației sunt verificate înaintea formei: sub ele stau și adrese cu token.
        if (segments[0] is not ("masini" or "f") && CompanySlug.IsReserved(segments[0]))
        {
            return PageHead.None;
        }

        // Un slug e scris doar cu litere mici, cifre și cratime. Orice altceva (un fișier căutat de un
        // robot, o cale prea adâncă) nu e o pagină și nu merită o interogare.
        if (segments.Length > 2 || !segments.All(IsSlug))
        {
            return Result.Failure<PageHead>(Missing);
        }

        PageHead? head = segments switch
        {
            // „/masini” singură e lista, o pagină a aplicației.
            ["masini"] or ["f"] => PageHead.None,
            ["masini", var carSlug] => await CarAsync(carSlug, companySlug: null, site, cancellationToken),
            ["f", var companySlug] => await CompanyAsync(companySlug, site, cancellationToken),
            [var companySlug] => await CompanyAsync(companySlug, site, cancellationToken),
            [var companySlug, var carSlug] => await CarAsync(carSlug, companySlug, site, cancellationToken),
            _ => null,
        };

        return head is null ? Result.Failure<PageHead>(Missing) : head;
    }

    private async Task<PageHead?> CarAsync(string slug, string? companySlug, string site, CancellationToken cancellationToken)
    {
        var car = await context.Cars
            .AsNoTracking()
            .Where(c => c.Slug == slug)
            .Where(CarVisibility.IsPublic)
            .Select(c => new
            {
                c.Brand,
                c.Model,
                c.Year,
                c.Location,
                c.Transmission,
                c.Engine,
                c.PricePerWeek,
                c.PostedByUserId,
                Image = c.Images.OrderBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (car is null)
        {
            return null;
        }

        // Din mini-site-ul unei firme se deschid doar mașinile ei.
        if (companySlug is not null)
        {
            bool isTheirs = car.PostedByUserId is { } owner
                && await context.CompanyProfiles.AnyAsync(p => p.UserId == owner && p.Slug == companySlug, cancellationToken);
            if (!isTheirs)
            {
                return null;
            }
        }

        string name = $"{car.Brand} {car.Model} {car.Year.ToString(CultureInfo.InvariantCulture)}";
        string price = Math.Round(car.PricePerWeek, MidpointRounding.AwayFromZero).ToString("N", Lei);
        string title = $"{name} de închiriat în {car.Location} — {price} lei/săptămână";
        string description =
            $"{name}, {car.Transmission.ToLower(Romanian)}, {car.Engine.ToLower(Romanian)}, disponibilă în " +
            $"{car.Location} pentru ridesharing. {price} lei pe săptămână, fără plată online.";

        // Adresa canonică e mereu /masini/{slug}, oricare ar fi firma din care s-a deschis mașina.
        return Build(title, description, $"{site}/masini/{slug}", "product", ImageUrl(car.Image, site));
    }

    private async Task<PageHead?> CompanyAsync(string slug, string site, CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await context.CompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug, cancellationToken);

        if (profile is null)
        {
            return null;
        }

        // Aceleași reguli ca pagina publică (`GetPublicCompanyQuery`): textul vine din copia aprobată
        // și dispare dacă secțiunea a fost oprită din administrare.
        CompanyPagePublication published = profile.PublishedPage.ApprovedAtUtc.HasValue
            ? profile.PublishedPage
            : new CompanyPagePublication();
        string? about = profile.PageModeration.BlockedSections.Contains(CompanyPageSections.About)
            ? null
            : published.PublicDescription;

        return Build(
            profile.LegalName,
            CompanyDescription(profile.LegalName, published.Tagline, about),
            $"{site}/{slug}",
            "website",
            image: null);
    }

    /// <summary>Sloganul firmei; altfel începutul descrierii ei; altfel o propoziție despre ce e pagina.</summary>
    internal static string CompanyDescription(string legalName, string? tagline, string? about)
    {
        string text = Collapse(tagline);
        if (text.Length == 0)
        {
            text = Collapse(about);
        }

        if (text.Length == 0)
        {
            return $"{legalName} — mașini de închiriat pentru ridesharing, pe {SiteName}.";
        }

        return text.Length <= MaxDescriptionLength
            ? text
            : text[..(MaxDescriptionLength - 1)].TrimEnd() + "…";
    }

    private static string Collapse(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsSlug(string segment) =>
        segment.Length <= 200 && segment.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    /// <summary>
    /// Fotografiile stau la API, sub <c>/uploads/cars/</c>; site-ul le servește și el, de pe domeniul
    /// lui, tocmai ca imaginea de previzualizare să aibă o adresă care nu depinde de unde e API-ul.
    /// </summary>
    private static string? ImageUrl(string? path, string site)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : site + path;
    }

    private static PageHead Build(string pageTitle, string description, string url, string type, string? image)
    {
        string title = $"{pageTitle} • {SiteName}";
        var html = new StringBuilder();

        // Titlul și descrierea le înlocuiesc pe cele implicite ale paginii. Restul poartă `data-seo`,
        // ca aplicația să le recunoască drept ale ei și să le scoată la plecarea de pe pagină.
        html.Append("<title>").Append(Escape(title)).Append("</title>\n");
        Meta(html, "name", "description", description, managed: false);
        html.Append("<link rel=\"canonical\" href=\"").Append(Escape(url)).Append("\" data-seo=\"\" />\n");
        Meta(html, "property", "og:site_name", SiteName);
        Meta(html, "property", "og:locale", "ro_RO");
        Meta(html, "property", "og:type", type);
        Meta(html, "property", "og:title", title);
        Meta(html, "property", "og:description", description);
        Meta(html, "property", "og:url", url);
        Meta(html, "property", "og:image", image ?? $"{new Uri(url).GetLeftPart(UriPartial.Authority)}/icon-192.png");
        Meta(html, "name", "twitter:card", image is null ? "summary" : "summary_large_image");
        Meta(html, "name", "twitter:title", title);
        Meta(html, "name", "twitter:description", description);

        return new PageHead(html.ToString());
    }

    /// <summary>
    /// Doar caracterele care ar rupe eticheta. <c>WebUtility.HtmlEncode</c> ar scrie și diacriticele
    /// ca entități numerice — corect, dar pagina e UTF-8 și n-are de ce.
    /// </summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static void Meta(StringBuilder html, string attribute, string key, string content, bool managed = true) =>
        html.Append("<meta ").Append(attribute).Append("=\"").Append(key)
            .Append("\" content=\"").Append(Escape(content)).Append('"')
            .Append(managed ? " data-seo=\"\"" : string.Empty)
            .Append(" />\n");
}
