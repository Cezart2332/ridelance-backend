using System.Globalization;
using System.Text.Json;
using Application.Abstractions.Ai;
using Application.Accounting;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Infrastructure.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Accounting;

/// <summary>
/// Citirea facturilor și rapoartelor Uber/Bolt prin OpenRouter (spec contabilitate B1), cu
/// răspuns constrâns de o schemă JSON strictă.
/// </summary>
/// <remarks>
/// PDF-ul pleacă întotdeauna ca fișier, cu OCR-ul nativ al modelului: la un PDF fără text layer
/// modelul citește paginile ca imagine. Când există text layer, îl trimitem și ca text, ca
/// fragmentele sursă să fie copiate exact din el.
/// </remarks>
internal sealed class OpenRouterDocumentExtractor(
    HttpClient httpClient,
    IOptions<OpenRouterOptions> openRouter,
    IOptions<AccountingOptions> accounting,
    ILogger<OpenRouterDocumentExtractor> logger) : IDocumentExtractor
{
    private static readonly string[] FieldKeys =
    [
        "supplier_name", "supplier_country", "supplier_vat_id", "invoice_number", "invoice_date",
        "period_from", "period_to", "currency", "amount", "commission_amount",
    ];

    public async Task<Result<DocumentExtractionResult>> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken)
    {
        OpenRouterOptions config = openRouter.Value;
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return Result.Failure<DocumentExtractionResult>(Error.Failure("Ai.NotConfigured", "Cheia API OpenRouter nu este configurată."));
        }

        string model = accounting.Value.ExtractionModel is { Length: > 0 } configured ? configured : config.Model;
        string dataUrl = $"data:application/pdf;base64,{Convert.ToBase64String(request.FileBytes)}";
        string textPart = request.PdfText is null
            ? "PDF-ul nu are text selectabil: citește paginile ca imagine."
            : $"Textul extras din PDF (folosește-l pentru source_snippets, copiat exact):\n{request.PdfText}";

        var payload = new
        {
            model,
            reasoning = config.DocumentReasoning,
            temperature = 0,
            plugins = new object[] { new { id = "file-parser", pdf = new { engine = "native" } } },
            response_format = new
            {
                type = "json_schema",
                json_schema = new { name = "platform_document", strict = true, schema = Schema() },
            },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = $"Luna procesată: {request.Period}. Fișier: {request.FileName}.\n{textPart}" },
                        new { type = "file", file = new { filename = request.FileName, file_data = dataUrl } },
                    },
                },
            },
        };

        Result<string> body = await OpenRouterJson.SendAsync(httpClient, config, payload, logger, $"documentul {request.FileName}", cancellationToken);
        if (body.IsFailure)
        {
            return Result.Failure<DocumentExtractionResult>(body.Error);
        }

        Result<string> content = OpenRouterJson.ExtractContent(body.Value);
        return content.IsFailure
            ? Result.Failure<DocumentExtractionResult>(content.Error)
            : Parse(content.Value, model, accounting.Value.ExtractionPromptVersion);
    }

    /// <summary>
    /// Doar citire (spec §0 pct. 5): modelul nu decide nimic fiscal și nu verifică nimic; sumele și
    /// datele le raportează exact cum apar, iar verificările le face codul.
    /// </summary>
    internal const string SystemPrompt =
        "Ești un extractor de date pentru RIDElance. Primești o factură de comision sau un raport lunar de venituri " +
        "emis de Bolt sau Uber către un șofer PFA din România. Sarcina ta e STRICT citirea: nu calcula, nu corecta, " +
        "nu decide nimic fiscal. " +
        "document_type: COMMISSION_INVOICE pentru factura de comision a platformei, PLATFORM_REPORT pentru raportul " +
        "de venituri / câștiguri, UNKNOWN pentru orice altceva. platform: BOLT sau UBER, după emitent; null dacă nu e clar. " +
        "Câmpuri: supplier_name (entitatea emitentă, ex. „Bolt Operations OÜ”), supplier_country (cod ISO din 2 litere), " +
        "supplier_vat_id (codul de TVA al emitentului, cu prefixul de țară), invoice_number, invoice_date " +
        "(data facturii sau a raportului), period_from și period_to (perioada serviciului), currency (cod ISO din 3 litere), " +
        "amount (la factură: totalul de plată; la raport: venitul brut din curse), commission_amount (comisionul platformei), " +
        "other_amounts (alte sume, de ex. TVA, cu eticheta lor). " +
        "Datele în format YYYY-MM-DD. Sumele ca numere, cu punct zecimal, fără separator de mii. " +
        "Pentru fiecare câmp completat, source_snippets conține textul EXACT din document din care l-ai citit " +
        "(inclusiv separatorii de mii și zecimale, ca în document); null dacă valoarea lipsește. " +
        "Dacă o valoare nu apare sau nu se citește cu certitudine, întoarce null — nu ghici. " +
        "confidence: încrederea ta globală, între 0 și 1.";

    internal static object Schema()
    {
        object nullableString = new { type = new[] { "string", "null" } };
        object nullableNumber = new { type = new[] { "number", "null" } };
        var fields = new Dictionary<string, object>
        {
            ["supplier_name"] = nullableString,
            ["supplier_country"] = nullableString,
            ["supplier_vat_id"] = nullableString,
            ["invoice_number"] = nullableString,
            ["invoice_date"] = nullableString,
            ["period_from"] = nullableString,
            ["period_to"] = nullableString,
            ["currency"] = nullableString,
            ["amount"] = nullableNumber,
            ["commission_amount"] = nullableNumber,
            ["other_amounts"] = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { label = new { type = "string" }, amount = new { type = "number" } },
                    required = new[] { "label", "amount" },
                    additionalProperties = false,
                },
            },
        };
        Dictionary<string, object> snippets = FieldKeys.ToDictionary(key => key, _ => nullableString);

        return new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["document_type"] = new { type = "string", @enum = new[] { "COMMISSION_INVOICE", "PLATFORM_REPORT", "UNKNOWN" } },
                ["platform"] = new { type = new[] { "string", "null" }, @enum = new object?[] { "BOLT", "UBER", null } },
                ["fields"] = new { type = "object", properties = fields, required = fields.Keys.ToArray(), additionalProperties = false },
                ["source_snippets"] = new { type = "object", properties = snippets, required = FieldKeys, additionalProperties = false },
                ["confidence"] = new { type = "number" },
            },
            required = new[] { "document_type", "platform", "fields", "source_snippets", "confidence" },
            additionalProperties = false,
        };
    }

    /// <summary>Interpretarea răspunsului; tolerantă la formă (numere ca text, date cu oră).</summary>
    internal static Result<DocumentExtractionResult> Parse(string json, string model, string promptVersion)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement fields = root.GetProperty("fields");

            PlatformDocumentType type = Text(root, "document_type") switch
            {
                "COMMISSION_INVOICE" => PlatformDocumentType.CommissionInvoice,
                "PLATFORM_REPORT" => PlatformDocumentType.PlatformReport,
                _ => PlatformDocumentType.Unknown,
            };
            Platform? platform = Text(root, "platform") switch
            {
                "BOLT" => Platform.Bolt,
                "UBER" => Platform.Uber,
                _ => null,
            };

            var others = new List<OtherAmount>();
            if (fields.TryGetProperty("other_amounts", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                others.AddRange(list.EnumerateArray()
                    .Select(item => (Label: Text(item, "label"), Amount: Number(item, "amount")))
                    .Where(item => item.Label is not null && item.Amount is not null)
                    .Select(item => new OtherAmount(item.Label!, item.Amount!.Value)));
            }

            var extracted = new ExtractedFields(
                Text(fields, "supplier_name"),
                Text(fields, "supplier_country")?.ToUpperInvariant(),
                Text(fields, "supplier_vat_id")?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
                Text(fields, "invoice_number"),
                Date(fields, "invoice_date"),
                Date(fields, "period_from"),
                Date(fields, "period_to"),
                Text(fields, "currency")?.ToUpperInvariant(),
                Number(fields, "amount"),
                Number(fields, "commission_amount"),
                others);

            var snippets = new Dictionary<string, string>();
            if (root.TryGetProperty("source_snippets", out JsonElement source) && source.ValueKind == JsonValueKind.Object)
            {
                foreach (string key in FieldKeys)
                {
                    if (Text(source, key) is { } snippet)
                    {
                        snippets[JsonNamingPolicy.CamelCase.ConvertName(ToPascal(key))] = snippet;
                    }
                }
            }

            double? confidence = root.TryGetProperty("confidence", out JsonElement c) && c.ValueKind == JsonValueKind.Number
                ? Math.Clamp(c.GetDouble(), 0, 1)
                : null;

            return new DocumentExtractionResult(type, platform, extracted, snippets, confidence, model, promptVersion);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Result.Failure<DocumentExtractionResult>(Error.Failure("Ai.InvalidResponse", "Răspunsul citirii nu a putut fi interpretat."));
        }
    }

    private static string ToPascal(string snake) =>
        string.Concat(snake.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static decimal? Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDecimal();
        }

        return value.ValueKind == JsonValueKind.String &&
               decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : null;
    }

    private static DateOnly? Date(JsonElement element, string name)
    {
        string? text = Text(element, name);
        if (text is null || text.Length < 10)
        {
            return null;
        }

        return DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
    }
}
