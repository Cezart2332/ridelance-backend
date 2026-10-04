using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.FiscalLink;

/// <summary>
/// Documented integrator GET cash-registers/{serial}/commands and GET .../{commandId}.
/// Never issues printer commands. Only successful fiscal receipts and Z reports are imported.
/// </summary>
internal sealed class FiscalLinkAccountingService(
    HttpClient http,
    IFiscalLinkService management,
    IOptions<FiscalLinkOptions> options,
    ILogger<FiscalLinkAccountingService> logger) : IFiscalLinkAccountingService
{
    private const int PageSize = 100;
    private const int MaxPages = 1000;
    private readonly FiscalLinkOptions _options = options.Value;

    public bool IsConfigured => _options.AccountingConfigured;

    public async Task<Result<FiscalLinkCashDocuments>> ReadAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Result.Failure<FiscalLinkCashDocuments>(Error.Problem("FiscalLink.AccountingNotConfigured",
                "Importul contabil FiscalLink necesită cheile Gestiune și Comenzi configurate pe server."));
        }

        Result<IReadOnlyList<FiscalLinkRegister>> listed = await management.ListRegistersAsync(clientId, cancellationToken);
        if (listed.IsFailure)
        {
            return Result.Failure<FiscalLinkCashDocuments>(listed.Error);
        }

        var receipts = new List<FiscalLinkCashReceipt>();
        var reports = new List<FiscalLinkCashZ>();
        var notes = new List<string>();
        foreach (FiscalLinkRegister register in listed.Value.Where(r => !r.AwaitingClientConsent && !string.IsNullOrWhiteSpace(r.SerialNumber)))
        {
            string serial = register.SerialNumber!.Trim().ToUpperInvariant();
            string path = $"cash-registers/{Uri.EscapeDataString(serial)}/commands";
            bool complete = false;
            int received = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int page = 1; page <= MaxPages; page++)
            {
                Result<JsonElement> response = await GetAsync($"{path}?page={page}&pageSize={PageSize}", cancellationToken);
                if (response.IsFailure)
                {
                    // No partial batch on a failed page: the next sync retries the entire history.
                    return Result.Failure<FiscalLinkCashDocuments>(response.Error);
                }

                JsonElement root = response.Value;
                List<JsonElement>? items = Items(root);
                if (items is null)
                {
                    return Invalid("FiscalLink a trimis un istoric de comenzi într-un format necunoscut.");
                }
                received += items.Count;

                foreach (JsonElement item in items)
                {
                    if (Text(item, "status") != "Succeeded")
                    {
                        continue;
                    }

                    string? type = Text(item, "commandType");
                    if (type is not ("cashRegister.printFiscalReceipt" or "cashRegister.printReportZ"))
                    {
                        continue;
                    }

                    string? id = Text(item, "commandId");
                    if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                    {
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            notes.Add($"Casa {serial}: o comandă fiscală nu are identificator; nu a fost importată.");
                        }
                        continue;
                    }

                    JsonElement command = item;
                    if (Property(item, "body").ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                    {
                        Result<JsonElement> detail = await GetAsync($"{path}/{Uri.EscapeDataString(id)}", cancellationToken);
                        if (detail.IsFailure)
                        {
                            return Result.Failure<FiscalLinkCashDocuments>(detail.Error);
                        }
                        command = detail.Value;
                    }

                    if (Text(command, "commandId") != id || Text(command, "status") != "Succeeded" || Text(command, "commandType") != type ||
                        Text(command, "clientId") is { } returnedClient && returnedClient != clientId.ToString() ||
                        Text(command, "serialNumber") is { } returnedSerial && !string.Equals(returnedSerial.Trim(), serial, StringComparison.OrdinalIgnoreCase))
                    {
                        notes.Add($"Casa {serial}: răspunsul comenzii {id} nu corespunde documentului cerut.");
                        continue;
                    }

                    JsonElement body = Property(command, "body");
                    if (body.ValueKind == JsonValueKind.String)
                    {
                        try
                        {
                            using var parsed = JsonDocument.Parse(body.GetString()!);
                            body = parsed.RootElement.Clone();
                        }
                        catch (JsonException)
                        {
                            notes.Add($"Casa {serial}: documentul {id} nu conține date structurate.");
                            continue;
                        }
                    }
                    JsonElement data = Property(body, "data");
                    if (data.ValueKind == JsonValueKind.Object)
                    {
                        body = data;
                    }

                    string? number = Text(body, type == "cashRegister.printReportZ" ? "reportNumber" : "receiptNumber");
                    decimal? total = Number(body, "total") ?? Number(body, "totalAmount");
                    // Structured Z responses expose totals by tax group, as used by FiscalLink Cloud.
                    if (total is null && type == "cashRegister.printReportZ")
                    {
                        JsonElement groups = Property(body, "taxGroups");
                        if (groups.ValueKind == JsonValueKind.Object && groups.EnumerateObject().Any())
                        {
                            List<decimal?> amounts = [.. groups.EnumerateObject().Select(p => Decimal(p.Value))];
                            if (amounts.All(a => a is not null))
                            {
                                total = amounts.Sum(a => a!.Value);
                            }
                        }
                    }

                    // The RIDElance cash register records cash only; reject conflicting explicit card totals.
                    JsonElement payments = Property(body, "payments");
                    bool hasOtherPayments = payments.ValueKind == JsonValueKind.Object && payments.EnumerateObject()
                        .Any(p => !string.Equals(p.Name, "payment_method1", StringComparison.OrdinalIgnoreCase) && Decimal(p.Value) is { } amount && amount != 0);
                    DateTime? issued = Timestamp(body, "issuedAt") ?? Timestamp(command, "completedAt");
                    if (string.IsNullOrWhiteSpace(number) || number.Length > 32 || total is null or < 0 || issued is null || hasOtherPayments)
                    {
                        notes.Add($"Casa {serial}: documentul {id} necesită verificare (număr, dată, total cash sau tip de plată).");
                        continue;
                    }
                    if (register.ActivatedAtUtc is { } activated && issued < activated)
                    {
                        // A serial can have an older history before its activation for this client.
                        continue;
                    }

                    if (type == "cashRegister.printReportZ")
                    {
                        reports.Add(new FiscalLinkCashZ(serial, number.Trim(), issued.Value, total.Value, id, command.GetRawText()));
                    }
                    else
                    {
                        receipts.Add(new FiscalLinkCashReceipt(serial, number.Trim(), issued.Value, total.Value, id));
                    }
                }

                decimal? totalCount = Number(root, "totalCount") ?? Number(root, "total");
                int effectiveSize = (int)(Number(root, "pageSize") ?? PageSize);
                if (items.Count == 0 || (totalCount is { } count ? received >= count : items.Count < effectiveSize))
                {
                    complete = true;
                    break;
                }
            }
            if (!complete)
            {
                return Invalid("Istoricul FiscalLink depășește limita de import; sincronizarea nu a fost marcată completă.");
            }
        }

        return new FiscalLinkCashDocuments(receipts, reports, notes);
    }

    private async Task<Result<JsonElement>> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.BaseUrl, path));
        request.Headers.Add("X-Api-Key", _options.CommandKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Citirea documentelor FiscalLink: HTTP {Status}", (int)response.StatusCode);
                return Result.Failure<JsonElement>(Error.Problem("FiscalLink.AccountingReadFailed",
                    $"FiscalLink nu a permis citirea documentelor (cod {(int)response.StatusCode}). Verifică cheia Comenzi și accesul la casă."));
            }
            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Citirea documentelor FiscalLink a eșuat.");
            return Result.Failure<JsonElement>(Error.Problem("FiscalLink.AccountingUnavailable", "Documentele FiscalLink nu au putut fi citite. Reîncearcă sincronizarea."));
        }
    }

    private static Result<FiscalLinkCashDocuments> Invalid(string message) =>
        Result.Failure<FiscalLinkCashDocuments>(Error.Problem("FiscalLink.AccountingInvalidResponse", message));

    private static List<JsonElement>? Items(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return [.. root.EnumerateArray()];
        }
        return Property(root, "items") is { ValueKind: JsonValueKind.Array } items ? [.. items.EnumerateArray()] : null;
    }

    private static JsonElement Property(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        ? value.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value : default;

    private static string? Text(JsonElement value, string name) => Property(value, name) is var property &&
        property.ValueKind is JsonValueKind.String or JsonValueKind.Number ? property.ToString() : null;

    private static decimal? Decimal(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal amount))
        {
            return amount;
        }
        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) ? amount : null;
    }

    private static decimal? Number(JsonElement value, string name) => Decimal(Property(value, name));

    private static DateTime? Timestamp(JsonElement value, string name) => Text(value, name) is { } text &&
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset timestamp) ? timestamp.UtcDateTime : null;
}
