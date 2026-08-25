using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.CompanyLookup;

/// <summary>
/// Căutarea firmei în registrul public ANAF.
/// </summary>
/// <remarks>
/// API-ul e public și nu cere niciun fel de cheie. Primește o listă de perechi CUI + dată și
/// întoarce două liste, <c>found</c> și <c>notFound</c>; aici se cere mereu o singură firmă,
/// pentru că precompletarea unui formular e despre una singură.
///
/// Data cerută e ziua pentru care se vrea situația: se trimite ziua curentă, fiindcă factura se
/// emite azi, iar plătitor de TVA sau nu se stabilește la data emiterii.
/// </remarks>
internal sealed class AnafCompanyLookupService(
    HttpClient httpClient,
    ILogger<AnafCompanyLookupService> logger) : ICompanyLookupService
{
    private static readonly Uri Endpoint = new("https://webservicesp.anaf.ro/api/PlatitorTvaRest/v9/tva");

    public async Task<CompanyLookupResult?> FindByCuiAsync(string cui, CancellationToken cancellationToken = default)
    {
        string normalized = Normalize(cui);
        if (normalized.Length == 0 || !long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out long numericCui))
        {
            return null;
        }

        object[] body =
        [
            new Dictionary<string, object?>
            {
                ["cui"] = numericCui,
                ["data"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            },
        ];

        try
        {
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(Endpoint, body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("ANAF a răspuns {Status} pentru CUI {Cui}.", (int)response.StatusCode, normalized);
                return null;
            }

            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("found", out JsonElement found) || found.GetArrayLength() == 0)
            {
                return null;
            }

            return Read(found[0], normalized);
        }
        catch (HttpRequestException exception)
        {
            // Registrul indisponibil nu blochează emiterea: câmpurile rămân de completat de mână.
            logger.LogWarning(exception, "Nu am putut interoga ANAF pentru CUI {Cui}.", normalized);
            return null;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Interogarea ANAF pentru CUI {Cui} a expirat.", normalized);
            return null;
        }
    }

    private static CompanyLookupResult? Read(JsonElement entry, string cui)
    {
        if (!entry.TryGetProperty("date_generale", out JsonElement general))
        {
            return null;
        }

        string? name = Text(general, "denumire");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        bool vatPayer = entry.TryGetProperty("inregistrare_scop_Tva", out JsonElement vat)
            && vat.TryGetProperty("scpTVA", out JsonElement scpTva)
            && scpTva.ValueKind == JsonValueKind.True;

        // Sediul social e mai structurat decât `adresa` din datele generale, care vine ca un
        // singur șir. Se preferă el, cu adresa generală drept rezervă.
        entry.TryGetProperty("adresa_sediu_social", out JsonElement office);

        return new CompanyLookupResult(
            cui,
            name.Trim(),
            BuildAddress(office) ?? Text(general, "adresa"),
            Text(office, "sdenumire_Localitate"),
            Text(office, "sdenumire_Judet"),
            Text(general, "nrRegCom"),
            vatPayer);
    }

    /// <summary>Strada, numărul și restul, ca un singur rând — cum se scrie pe o factură.</summary>
    private static string? BuildAddress(JsonElement office)
    {
        if (office.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string[] parts =
        [
            Text(office, "sdenumire_Strada") ?? string.Empty,
            Text(office, "snumar_Strada") ?? string.Empty,
            Text(office, "sdetalii_Adresa") ?? string.Empty,
        ];

        string joined = string.Join(" ", parts.Where(part => part.Length > 0)).Trim();
        return joined.Length > 0 ? joined : null;
    }

    private static string? Text(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>„RO12345678" și „12345678" sunt același CUI; ANAF îl vrea fără prefix.</summary>
    private static string Normalize(string? cui) =>
        new((cui ?? string.Empty).Where(char.IsDigit).ToArray());
}
