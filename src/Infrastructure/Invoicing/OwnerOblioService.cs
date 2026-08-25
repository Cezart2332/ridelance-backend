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

    public async Task<OwnerInvoice> CreateInvoiceAsync(
        OwnerOblioCredentials credentials,
        NewOwnerInvoice invoice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(invoice);

        var body = new Dictionary<string, object?>
        {
            ["cif"] = credentials.Cif,
            ["seriesName"] = invoice.SeriesName,
            ["issueDate"] = invoice.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["dueDate"] = invoice.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["language"] = "RO",
            ["client"] = new Dictionary<string, object?>
            {
                ["name"] = invoice.ClientName,
                ["cif"] = invoice.ClientCif,
                ["email"] = invoice.ClientEmail,
                ["address"] = invoice.ClientAddress,
                ["city"] = invoice.ClientCity,
                ["state"] = invoice.ClientState,
                ["country"] = "Romania",
                // Clientul rămâne în nomenclatorul proprietarului: a doua factură către același
                // CUI se completează singură din Oblio, fără să fie rescris totul.
                ["save"] = true,
            },
            ["products"] = invoice.Lines.Select(line => new Dictionary<string, object?>
            {
                ["name"] = line.Name,
                ["price"] = line.PriceLei,
                ["quantity"] = line.Quantity,
                ["measuringUnit"] = line.MeasuringUnit,
                ["productType"] = "Serviciu",
                ["vatName"] = line.VatName,
                ["vatPercentage"] = line.VatPercent,
                ["vatIncluded"] = line.VatIncluded,
            }).ToList(),
            ["mentions"] = invoice.Note,
            // Fără email de client nu are unde să plece; cu email, Oblio o trimite cu șablonul lui.
            ["sendEmail"] = string.IsNullOrWhiteSpace(invoice.ClientEmail) ? 0 : 1,
        };

        JsonElement data = await OblioHttp.SendJsonAsync(
            httpClient,
            Map(credentials),
            HttpMethod.Post,
            "docs/invoice",
            body,
            cancellationToken);

        string seriesName = ReadString(data, "seriesName") ?? invoice.SeriesName;
        string number = ReadString(data, "number") ?? string.Empty;

        if (invoice.SendToSpv && number.Length > 0)
        {
            await SendToSpvAsync(credentials, seriesName, number, cancellationToken);
        }

        decimal total = invoice.Lines.Sum(line => line.Quantity * line.PriceLei);

        return new OwnerInvoice(
            seriesName,
            number,
            invoice.IssueDate,
            invoice.DueDate,
            invoice.ClientName,
            invoice.ClientCif,
            total,
            0m,
            ReadString(data, "link"),
            Canceled: false);
    }

    /// <summary>
    /// Depune factura în SPV.
    /// </summary>
    /// <remarks>
    /// Factura e deja emisă când se ajunge aici, deci un eșec la SPV nu are voie s-o anuleze.
    /// Excepția urcă totuși: proprietarul a cerut explicit depunerea, iar o depunere eșuată în
    /// tăcere l-ar lăsa să creadă că e depusă.
    /// </remarks>
    private async Task SendToSpvAsync(
        OwnerOblioCredentials credentials,
        string seriesName,
        string number,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["cif"] = credentials.Cif,
            ["seriesName"] = seriesName,
            ["number"] = number,
        };

        await OblioHttp.SendJsonAsync(httpClient, Map(credentials), HttpMethod.Post, "docs/einvoice", body, cancellationToken);
    }

    /// <summary>Oblio întoarce numărul când ca șir, când ca număr; ambele se citesc la fel.</summary>
    private static string? ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
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
