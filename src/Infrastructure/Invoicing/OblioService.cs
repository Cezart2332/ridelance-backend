using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Invoicing;

/// <summary>
/// Oblio.eu REST API client (https://www.oblio.eu/api).
/// Invoices are emailed to the client by Oblio (sendEmail). SPV (e-Factura)
/// submission is opt-in via Oblio:SendToSpv and never fails the invoice.
/// </summary>
internal sealed class OblioService(
    HttpClient httpClient,
    IOptions<OblioOptions> optionsAccessor,
    ILogger<OblioService> logger) : IOblioService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly OblioOptions _options = optionsAccessor.Value;

    /// <summary>Credențialele platformei. Proprietarii au propriile lor, vezi `OwnerOblioService`.</summary>
    private OblioCredentials Credentials =>
        new(_options.ClientId, _options.ClientSecret, _options.Cif, _options.BaseUrl);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ClientId) &&
        !string.IsNullOrWhiteSpace(_options.ClientSecret) &&
        !string.IsNullOrWhiteSpace(_options.Cif) &&
        !string.IsNullOrWhiteSpace(_options.SeriesName);

    public string? Cif => string.IsNullOrWhiteSpace(_options.Cif) ? null : _options.Cif;

    public string? SeriesName => string.IsNullOrWhiteSpace(_options.SeriesName) ? null : _options.SeriesName;

    public async Task<OblioConnectionInfo> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        string token = await GetAccessTokenAsync(cancellationToken);

        // Companies — verifies auth + that the configured CIF exists in the account.
        JsonElement companies = await GetAsync($"nomenclature/companies", token, cancellationToken);
        string? companyName = null;
        foreach (JsonElement company in companies.EnumerateArray())
        {
            string? cif = company.TryGetProperty("cif", out JsonElement cifEl) ? cifEl.GetString() : null;
            if (string.Equals(OblioHttp.NormalizeCif(cif), OblioHttp.NormalizeCif(_options.Cif), StringComparison.OrdinalIgnoreCase))
            {
                companyName = company.TryGetProperty("company", out JsonElement nameEl) ? nameEl.GetString() : null;
                break;
            }
        }

        if (companyName is null)
        {
            throw new OblioApiException(
                $"CIF-ul configurat ({_options.Cif}) nu a fost găsit între firmele contului Oblio.");
        }

        // Invoice series for the company.
        JsonElement series = await GetAsync(
            $"nomenclature/series?cif={Uri.EscapeDataString(_options.Cif)}",
            token,
            cancellationToken);

        var invoiceSeries = new List<string>();
        foreach (JsonElement s in series.EnumerateArray())
        {
            string? type = s.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() : null;
            string? name = s.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : null;
            if (name is not null && (type is null || type.Contains("Factura", StringComparison.OrdinalIgnoreCase)))
            {
                invoiceSeries.Add(name);
            }
        }

        return new OblioConnectionInfo(companyName, _options.Cif, invoiceSeries);
    }

    public async Task<OblioInvoiceResult> CreateInvoiceAsync(
        OblioInvoiceClient client,
        IReadOnlyList<OblioInvoiceLine> lines,
        string? internalNote = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        string token = await GetAccessTokenAsync(cancellationToken);

        var romaniaZone = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime todayRomania = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, romaniaZone).Date;

        var body = new Dictionary<string, object?>
        {
            ["cif"] = _options.Cif,
            ["seriesName"] = _options.SeriesName,
            ["issueDate"] = todayRomania.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["language"] = "RO",
            ["client"] = new Dictionary<string, object?>
            {
                ["name"] = client.Name,
                ["cif"] = client.Cif,
                ["email"] = client.Email,
                ["address"] = client.Address,
                ["city"] = client.City,
                ["state"] = client.State,
                ["country"] = "Romania",
                ["save"] = false,
            },
            ["products"] = lines.Select(line => new Dictionary<string, object?>
            {
                ["name"] = line.Name,
                ["price"] = line.PriceLei,
                ["quantity"] = line.Quantity,
                ["measuringUnit"] = "buc",
                ["productType"] = "Serviciu",
                ["vatIncluded"] = true,
            }).ToList(),
            ["internalNote"] = internalNote,
            // Oblio trimite factura pe emailul clientului, cu șablonul din
            // Setări → Email-uri alarme → Document pe email.
            ["sendEmail"] = string.IsNullOrWhiteSpace(client.Email) ? 0 : 1,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("docs/invoice"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body, options: JsonOptions);

        JsonElement data = await OblioHttp.SendAndReadDataAsync(httpClient, request, cancellationToken);

        string seriesName = data.TryGetProperty("seriesName", out JsonElement sn) ? sn.GetString() ?? _options.SeriesName : _options.SeriesName;
        string number = data.TryGetProperty("number", out JsonElement nr) ? nr.ToString() : string.Empty;
        string link = data.TryGetProperty("link", out JsonElement lk) ? lk.GetString() ?? string.Empty : string.Empty;

        if (_options.SendToSpv && !string.IsNullOrEmpty(number))
        {
            await TrySendToSpvAsync(seriesName, number, token, cancellationToken);
        }

        return new OblioInvoiceResult(seriesName, number, link);
    }

    /// <summary>
    /// Trimite factura la SPV (e-Factura). Factura e deja emisă, deci un eșec
    /// aici doar se loghează — nu invalidează emiterea.
    /// </summary>
    private async Task TrySendToSpvAsync(string seriesName, string number, string token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("docs/einvoice"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(
                new Dictionary<string, object?>
                {
                    ["cif"] = _options.Cif,
                    ["seriesName"] = seriesName,
                    ["number"] = number,
                },
                options: JsonOptions);

            HttpResponseMessage response = await httpClient.SendAsync(request, ct);
            string payload = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Factura {Series}-{Number} a fost trimisă la SPV.", seriesName, number);
            }
            else
            {
                logger.LogWarning(
                    "Trimiterea facturii {Series}-{Number} la SPV a eșuat ({StatusCode}): {Error}",
                    seriesName, number, (int)response.StatusCode, OblioHttp.ExtractErrorMessage(payload));
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Trimiterea facturii {Series}-{Number} la SPV a eșuat.", seriesName, number);
        }
    }

    // ── HTTP helpers ─────────────────────────────────────────────────────────

    private Task<string> GetAccessTokenAsync(CancellationToken ct) =>
        OblioHttp.GetAccessTokenAsync(httpClient, Credentials, ct);

    private async Task<JsonElement> GetAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await OblioHttp.SendAndReadDataAsync(httpClient, request, ct);
    }

    private Uri BuildUri(string path) => OblioHttp.BuildUri(Credentials, path);

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new OblioApiException(
                "Integrarea Oblio nu este configurată. Setează Oblio:ClientId, Oblio:ClientSecret, Oblio:Cif și Oblio:SeriesName.");
        }
    }


}
