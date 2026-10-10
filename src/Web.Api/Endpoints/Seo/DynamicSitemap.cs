using Application.Abstractions.Messaging;
using Application.Seo;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Seo;

/// <summary>
/// Sitemap-ul adreselor care vin din date (mașini, pagini de firme). Frontendul îl servește mai
/// departe la <c>https://ridelance.ro/sitemap-anunturi.xml</c>, ca toate sitemap-urile să stea pe
/// domeniul site-ului.
/// </summary>
internal sealed class DynamicSitemap : IEndpoint
{
    private const string DefaultSiteOrigin = "https://ridelance.ro";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("sitemap.xml", async (
            IConfiguration configuration,
            IQueryHandler<GetDynamicSitemapQuery, string> handler,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            string siteOrigin = configuration["App:BaseUrl"] is { Length: > 0 } configured ? configured : DefaultSiteOrigin;
            Result<string> result = await handler.Handle(new GetDynamicSitemapQuery(siteOrigin), cancellationToken);
            if (result.IsFailure)
            {
                return CustomResults.Problem(result);
            }

            // O oră de cache: lista se schimbă rar, iar crawlerele o cer des.
            http.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Text(result.Value, "application/xml; charset=utf-8");
        })
        .AllowAnonymous()
        .WithTags("Seo");
    }
}
