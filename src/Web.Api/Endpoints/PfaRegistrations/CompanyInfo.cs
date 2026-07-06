using System.Globalization;
using System.Text.Json;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>
/// Looks up public company data (name, address) for a Romanian CUI via the
/// ANAF web service, so "Am PFA" onboarding can pre-fill real company data.
/// </summary>
internal sealed class CompanyInfo : IEndpoint
{
    public sealed record CompanyInfoResponse(
        string Cui,
        string Name,
        string? Address,
        string? Street,
        string? StreetNumber,
        string? City,
        string? County,
        string? RegistrationDate,
        bool IsActive);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("pfa-registrations/company-info/{cui}", async (
            string cui,
            IHttpClientFactory httpClientFactory,
            CancellationToken cancellationToken) =>
        {
            string digits = new([.. cui.Trim().ToUpperInvariant().Replace("RO", "", StringComparison.Ordinal).Where(char.IsDigit)]);
            if (digits.Length is < 2 or > 10 || !long.TryParse(digits, out long cuiNumber))
            {
                return Results.Problem(statusCode: 400, detail: "CUI invalid.");
            }

            HttpClient client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var payload = new[]
            {
                new { cui = cuiNumber, data = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
            };

            HttpResponseMessage response;
            try
            {
#pragma warning disable S1075 // URIs should not be hardcoded — public ANAF web service endpoint
                response = await client.PostAsJsonAsync(
                    "https://webservicesp.anaf.ro/PlatitorTvaRest/api/v9/ws/tva",
                    payload,
                    cancellationToken);
#pragma warning restore S1075
            }
            catch (Exception)
            {
                return Results.Problem(statusCode: 502, detail: "Serviciul ANAF nu a putut fi contactat. Încearcă din nou.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return Results.Problem(statusCode: 502, detail: "Serviciul ANAF a răspuns cu eroare. Încearcă din nou.");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!doc.RootElement.TryGetProperty("found", out JsonElement found) || found.GetArrayLength() == 0)
            {
                return Results.Problem(statusCode: 404, detail: "Nu am găsit nicio firmă înregistrată cu acest CUI.");
            }

            JsonElement entry = found[0];
            JsonElement general = entry.GetProperty("date_generale");

            static string? GetString(JsonElement element, string property) =>
                element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;

            string name = GetString(general, "denumire") ?? string.Empty;
            string? address = GetString(general, "adresa");
            string? registrationDate = GetString(general, "data_inregistrare");
            string stareInregistrare = GetString(general, "stare_inregistrare") ?? string.Empty;
            bool isActive = !stareInregistrare.Contains("RADIERE", StringComparison.OrdinalIgnoreCase)
                && !stareInregistrare.Contains("INACTIV", StringComparison.OrdinalIgnoreCase);

            string? street = null;
            string? streetNumber = null;
            string? city = null;
            string? county = null;
            if (entry.TryGetProperty("adresa_sediu_social", out JsonElement sediu))
            {
                street = GetString(sediu, "sdenumire_Strada")?.Trim();
                streetNumber = GetString(sediu, "snumar_Strada")?.Trim();
                city = GetString(sediu, "sdenumire_Localitate")?.Trim();
                county = GetString(sediu, "sdenumire_Judet")?.Trim();
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.Problem(statusCode: 404, detail: "Nu am găsit nicio firmă înregistrată cu acest CUI.");
            }

            var result = new CompanyInfoResponse(
                digits,
                name,
                address,
                street,
                streetNumber,
                city,
                county,
                registrationDate,
                isActive);

            return Results.Ok(result);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);
    }
}
