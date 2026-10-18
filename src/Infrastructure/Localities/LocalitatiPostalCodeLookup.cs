using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Application.Abstractions.Services;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Localities;

/// <summary>
/// Codul poștal prin <c>GET /search?q=&amp;county=</c>. Răspunsurile stau în memorie: lista de
/// localități se schimbă de câteva ori pe an, iar planul gratuit are 100 de cereri pe zi.
/// </summary>
internal sealed partial class LocalitatiPostalCodeLookup(
    HttpClient httpClient,
    IMemoryCache cache,
    IOptions<LocalitatiOptions> options,
    ILogger<LocalitatiPostalCodeLookup> logger) : IPostalCodeLookup
{
    private static readonly TimeSpan FoundTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan NotFoundTtl = TimeSpan.FromDays(1);

    public async Task<string?> FindAsync(string judet, string localitate, CancellationToken cancellationToken)
    {
        string? county = Judete.Code(judet);
        string query = LocalityQuery(localitate);
        if (county is null || query.Length == 0)
        {
            return null;
        }

        string cacheKey = $"postal:{county}:{Judete.Fold(query)}";
        if (cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached;
        }

        SearchResponse? response;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(options.Value.BaseUrl, $"search?q={Uri.EscapeDataString(query)}&county={county}"));

            if (!string.IsNullOrWhiteSpace(options.Value.ApiKey))
            {
                request.Headers.Add("X-API-Key", options.Value.ApiKey);
            }

            using HttpResponseMessage http = await httpClient.SendAsync(request, cancellationToken);
            if (!http.IsSuccessStatusCode)
            {
                // Limita zilnică, cheie greșită: omul scrie codul de mână, nu se blochează nimic.
                logger.LogWarning("localitati.dev a răspuns {Status} pentru {County}/{Query}.", (int)http.StatusCode, county, query);
                return null;
            }

            response = await http.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "localitati.dev nu a răspuns pentru {County}/{Query}.", county, query);
            return null;
        }

        // Doar localitatea cerută, nu cea mai apropiată ca scriere: un cod de la alt sat e mai rău
        // decât un câmp gol.
        string wanted = Judete.Fold(query);
        string? code = response?.Results
            .Where(r => r.PostalCode is { Length: 6 } && Judete.Fold(r.Name ?? string.Empty) == wanted)
            .Select(r => r.PostalCode)
            .FirstOrDefault();

        cache.Set(cacheKey, code, code is null ? NotFoundTtl : FoundTtl);
        return code;
    }

    /// <summary>
    /// Numele localității așa cum îl caută API-ul: fără „Mun.", „Or.", „Com.", „Sat", iar în
    /// București sectorul („Sec.3" → „Sectorul 3").
    /// </summary>
    internal static string LocalityQuery(string localitate)
    {
        string value = localitate.Trim();

        Match sector = SectorPattern().Match(value);
        if (sector.Success)
        {
            return $"Sectorul {sector.Groups[1].Value}";
        }

        return PrefixPattern().Replace(value, string.Empty).Trim();
    }

    [GeneratedRegex(@"\bsec(?:t(?:or(?:ul)?)?)?\.?\s*([1-6])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SectorPattern();

    [GeneratedRegex(@"^(?:mun(?:icipiul)?|or(?:a[sș]ul|a[sș])?|com(?:una)?|sat(?:ul)?|loc(?:alitatea)?)(?:\.\s*|\s+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();

    private sealed record SearchResponse(
        [property: JsonPropertyName("results")] List<SearchResult> Results);

    private sealed record SearchResult(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("postal_code")] string? PostalCode);
}
