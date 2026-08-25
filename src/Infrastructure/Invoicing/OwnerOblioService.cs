using System.Globalization;
using System.Text.Json;
using Application.Abstractions.Services;
using Microsoft.Extensions.Options;

namespace Infrastructure.Invoicing;

/// <summary>
/// Facturarea pe contul Oblio al unui proprietar.
/// </summary>
/// <remarks>
/// Folosește același transport ca facturarea platformei (<see cref="OblioHttp"/>), doar cu alte
/// credențiale. `BaseUrl` rămâne din configurare: e adresa API-ului Oblio, nu ceva ce alege
/// proprietarul.
/// </remarks>
internal sealed class OwnerOblioService(
    HttpClient httpClient,
    IOptions<OblioOptions> optionsAccessor) : IOwnerInvoicingService
{
    private readonly string _baseUrl = optionsAccessor.Value.BaseUrl;

    private OblioCredentials Map(OwnerOblioCredentials credentials) =>
        new(credentials.ClientId, credentials.ClientSecret, credentials.Cif, _baseUrl);

    public async Task<OblioConnectionInfo> TestConnectionAsync(
        OwnerOblioCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        OblioCredentials mapped = Map(credentials);

        JsonElement companies = await OblioHttp.GetAsync(httpClient, mapped, "nomenclature/companies", cancellationToken);

        string? companyName = null;
        foreach (JsonElement company in companies.EnumerateArray())
        {
            string? cif = company.TryGetProperty("cif", out JsonElement cifEl) ? cifEl.GetString() : null;
            if (string.Equals(OblioHttp.NormalizeCif(cif), OblioHttp.NormalizeCif(credentials.Cif), StringComparison.OrdinalIgnoreCase))
            {
                companyName = company.TryGetProperty("company", out JsonElement nameEl) ? nameEl.GetString() : null;
                break;
            }
        }

        if (companyName is null)
        {
            throw new OblioApiException(
                $"CIF-ul {credentials.Cif} nu apare între firmele acestui cont Oblio.");
        }

        JsonElement series = await OblioHttp.GetAsync(
            httpClient,
            mapped,
            $"nomenclature/series?cif={Uri.EscapeDataString(credentials.Cif)}",
            cancellationToken);

        var invoiceSeries = new List<string>();
        foreach (JsonElement item in series.EnumerateArray())
        {
            string? type = item.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() : null;
            string? name = item.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() : null;
            if (name is not null && (type is null || type.Contains("Factura", StringComparison.OrdinalIgnoreCase)))
            {
                invoiceSeries.Add(name);
            }
        }

        return new OblioConnectionInfo(companyName, credentials.Cif, invoiceSeries);
    }

    public async Task<IReadOnlyList<OwnerInvoice>> ListInvoicesAsync(
        OwnerOblioCredentials credentials,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        string path = "docs/invoice/list"
            + $"?cif={Uri.EscapeDataString(credentials.Cif)}"
            + $"&issuedAfter={from:yyyy-MM-dd}"
            + $"&issuedBefore={to:yyyy-MM-dd}";

        JsonElement data = await OblioHttp.GetAsync(httpClient, Map(credentials), path, cancellationToken);

        var invoices = new List<OwnerInvoice>();
        if (data.ValueKind != JsonValueKind.Array)
        {
            return invoices;
        }

        foreach (JsonElement item in data.EnumerateArray())
        {
            OwnerInvoice? invoice = OblioInvoiceParser.Parse(item);
            if (invoice is not null)
            {
                invoices.Add(invoice);
            }
        }

        return invoices;
    }

    public async Task CollectAsync(
        OwnerOblioCredentials credentials,
        string seriesName,
        string number,
        decimal amountLei,
        string paymentMethod,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var body = new Dictionary<string, object?>
        {
            ["cif"] = credentials.Cif,
            ["seriesName"] = seriesName,
            ["number"] = number,
            ["collect"] = new Dictionary<string, object?>
            {
                ["type"] = paymentMethod,
                ["value"] = amountLei,
                ["documentDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            },
        };

        await OblioHttp.SendJsonAsync(httpClient, Map(credentials), HttpMethod.Put, "docs/invoice/collect", body, cancellationToken);
    }

    public async Task CancelAsync(
        OwnerOblioCredentials credentials,
        string seriesName,
        string number,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var body = new Dictionary<string, object?>
        {
            ["cif"] = credentials.Cif,
            ["seriesName"] = seriesName,
            ["number"] = number,
        };

        await OblioHttp.SendJsonAsync(httpClient, Map(credentials), HttpMethod.Put, "docs/invoice/cancel", body, cancellationToken);
    }
}
