using Application.Abstractions.Messaging;
using Application.Seo;
using SharedKernel;

namespace Web.Api.Endpoints.Seo;

/// <summary>
/// Întrebarea pe care o pune serverul site-ului pentru o adresă care nu e un fișier: există pagina și
/// ce are în <c>&lt;head&gt;</c>? Vezi <see cref="GetPageHeadQuery"/> și <c>nginx/default.conf</c>.
/// </summary>
internal sealed class PageHeadEndpoint : IEndpoint
{
    private const string DefaultSiteOrigin = "https://ridelance.ro";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("seo/head", async (
            string? path,
            IConfiguration configuration,
            IQueryHandler<GetPageHeadQuery, PageHead> handler,
            CancellationToken cancellationToken) =>
        {
            string siteOrigin = configuration["App:BaseUrl"] is { Length: > 0 } configured ? configured : DefaultSiteOrigin;
            Result<PageHead> result = await handler.Handle(new GetPageHeadQuery(path ?? "/", siteOrigin), cancellationToken);

            // Fără corp la 404 și la „fără etichete”: răspunsul e citit de nginx, nu de un om.
            if (result.IsFailure)
            {
                return Results.NotFound();
            }

            return result.Value.Html.Length == 0
                ? Results.NoContent()
                : Results.Text(result.Value.Html, "text/html; charset=utf-8");
        })
        .AllowAnonymous()
        .WithTags("Seo");
    }
}
