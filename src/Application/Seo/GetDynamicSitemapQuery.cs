using System.Security;
using System.Text;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Seo;

/// <summary>
/// <c>GET /sitemap.xml</c> — adresele publice care vin din date: anunțurile de mașini vizibile în
/// marketplace și paginile firmelor care au cel puțin un astfel de anunț. Paginile statice au
/// sitemap-ul lor, în frontend.
///
/// Fără <c>lastmod</c>: mașina nu are o dată a ultimei schimbări care să conteze pentru cititor
/// (<c>UpdatedAtUtc</c> se mișcă și la recalcularea scorului), iar un <c>lastmod</c> care minte e mai
/// rău decât unul lipsă.
/// </summary>
/// <param name="SiteOrigin">Adresa publică a site-ului, fără slash final.</param>
public sealed record GetDynamicSitemapQuery(string SiteOrigin) : IQuery<string>;

internal sealed class GetDynamicSitemapQueryHandler(IApplicationDbContext context) : IQueryHandler<GetDynamicSitemapQuery, string>
{
    /// <summary>Limita protocolului pentru un singur fișier.</summary>
    private const int MaxUrls = 50_000;

    public async Task<Result<string>> Handle(GetDynamicSitemapQuery query, CancellationToken cancellationToken)
    {
        var cars = await context.Cars
            .AsNoTracking()
            .Where(CarVisibility.IsPublic)
            .OrderBy(c => c.Slug)
            .Select(c => new { c.Slug, c.PostedByUserId })
            .Take(MaxUrls)
            .ToListAsync(cancellationToken);

        // Pagina unei firme intră doar dacă are ce arăta: o firmă fără anunțuri publice ar fi o pagină goală.
        var owners = cars.Where(c => c.PostedByUserId != null).Select(c => c.PostedByUserId!.Value).Distinct().ToList();
        List<string> companies = await context.CompanyProfiles
            .AsNoTracking()
            .Where(p => owners.Contains(p.UserId) && p.Slug != null && p.Slug != "")
            .OrderBy(p => p.Slug)
            .Select(p => p.Slug)
            .ToListAsync(cancellationToken);

        string site = query.SiteOrigin.TrimEnd('/');
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        xml.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");

        // Adresa canonică a unei mașini e /masini/{slug}, oricare ar fi firma care o are.
        foreach (string slug in cars.Select(c => c.Slug).Where(slug => !string.IsNullOrWhiteSpace(slug)))
        {
            Append(xml, $"{site}/masini/{slug}");
        }

        foreach (string slug in companies)
        {
            Append(xml, $"{site}/{slug}");
        }

        xml.Append("</urlset>\n");
        return xml.ToString();
    }

    private static void Append(StringBuilder xml, string url) =>
        xml.Append("  <url><loc>").Append(SecurityElement.Escape(url)).Append("</loc></url>\n");
}
