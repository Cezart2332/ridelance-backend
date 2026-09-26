using System.Globalization;
using System.Text.Json;
using Application.Abstractions.Ai;
using Application.Accounting;
using Infrastructure.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Accounting;

/// <summary>
/// Citirea documentelor de cheltuială și a rapoartelor Z prin OpenRouter, cu schemă JSON strictă
/// (spec contabilitate B6). Modelul doar citește; tot ce e fiscal decide codul.
/// </summary>
internal sealed class OpenRouterReceiptExtractor(
    HttpClient httpClient,
    IOptions<OpenRouterOptions> openRouter,
    IOptions<AccountingOptions> accounting,
    ILogger<OpenRouterReceiptExtractor> logger) : IReceiptExtractor
{
    internal const string ExpensePrompt =
        "Ești un extractor de date pentru RIDElance. Primești un bon fiscal sau o factură de cheltuială a unui șofer PFA " +
        "din România (combustibil, service, telefon etc.). Sarcina ta e STRICT citirea: nu calcula, nu clasifica, nu decide nimic fiscal. " +
        "merchant: numele comerciantului; merchant_cui: codul fiscal al comerciantului, doar cifrele; date: data documentului, YYYY-MM-DD; " +
        "total: totalul de plată, număr cu punct zecimal; items: denumirile produselor sau serviciilor, cum apar. " +
        "Dacă o valoare nu se citește cu certitudine, întoarce null — nu ghici.";

    internal const string ZReportPrompt =
        "Ești un extractor de date pentru RIDElance. Primești un raport Z (raport fiscal de închidere zilnică) al unei case de marcat " +
        "din România. Sarcina ta e STRICT citirea: nu calcula nimic. date: data raportului, YYYY-MM-DD; z_number: numărul raportului Z, " +
        "doar cifrele; total: totalul încasărilor zilei, număr cu punct zecimal. Dacă o valoare nu se citește cu certitudine, întoarce null.";

    public async Task<Result<ExpenseReceiptReading>> ReadExpenseAsync(ReceiptExtractionRequest request, CancellationToken cancellationToken)
    {
        object nullableString = new { type = new[] { "string", "null" } };
        var properties = new Dictionary<string, object>
        {
            ["merchant"] = nullableString,
            ["merchant_cui"] = nullableString,
            ["date"] = nullableString,
            ["total"] = new { type = new[] { "number", "null" } },
            ["items"] = new { type = "array", items = new { type = "string" } },
        };

        Result<JsonElement> read = await ReadAsync(request, "expense_document", ExpensePrompt, properties, cancellationToken);
        if (read.IsFailure)
        {
            return Result.Failure<ExpenseReceiptReading>(read.Error);
        }

        JsonElement root = read.Value;
        List<string> items = root.TryGetProperty("items", out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).Where(item => item.Length > 0)]
            : [];
        return new ExpenseReceiptReading(
            Text(root, "merchant"),
            Text(root, "merchant_cui") is { } cui ? new string([.. cui.Where(char.IsAsciiDigit)]) : null,
            Date(root, "date"),
            Number(root, "total"),
            items);
    }

    public async Task<Result<ZReportReading>> ReadZReportAsync(ReceiptExtractionRequest request, CancellationToken cancellationToken)
    {
        object nullableString = new { type = new[] { "string", "null" } };
        var properties = new Dictionary<string, object>
        {
            ["date"] = nullableString,
            ["z_number"] = nullableString,
            ["total"] = new { type = new[] { "number", "null" } },
        };

        Result<JsonElement> read = await ReadAsync(request, "z_report", ZReportPrompt, properties, cancellationToken);
        return read.IsFailure
            ? Result.Failure<ZReportReading>(read.Error)
            : new ZReportReading(Date(read.Value, "date"), Text(read.Value, "z_number"), Number(read.Value, "total"));
    }

    private async Task<Result<JsonElement>> ReadAsync(
        ReceiptExtractionRequest request,
        string schemaName,
        string prompt,
        Dictionary<string, object> properties,
        CancellationToken cancellationToken)
    {
        OpenRouterOptions config = openRouter.Value;
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return Result.Failure<JsonElement>(Error.Failure("Ai.NotConfigured", "Cheia API OpenRouter nu este configurată."));
        }

        bool pdf = request.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) || request.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        string image = request.ContentType is { Length: > 0 } type ? type : "image/jpeg";
        string mediaType = pdf ? "application/pdf" : image;
        string dataUrl = $"data:{mediaType};base64,{Convert.ToBase64String(request.FileBytes)}";
        object file = pdf
            ? new { type = "file", file = new { filename = request.FileName, file_data = dataUrl } }
            : new { type = "image_url", image_url = new { url = dataUrl } };

        var payload = new
        {
            model = accounting.Value.ExtractionModel is { Length: > 0 } configured ? configured : config.Model,
            reasoning = config.DocumentReasoning,
            temperature = 0,
            plugins = pdf ? new object[] { new { id = "file-parser", pdf = new { engine = "native" } } } : null,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = schemaName,
                    strict = true,
                    schema = new { type = "object", properties, required = properties.Keys.ToArray(), additionalProperties = false },
                },
            },
            messages = new object[]
            {
                new { role = "system", content = prompt },
                new { role = "user", content = new object[] { new { type = "text", text = $"Fișier: {request.FileName}." }, file } },
            },
        };

        Result<string> body = await OpenRouterJson.SendAsync(httpClient, config, payload, logger, $"documentul {request.FileName}", cancellationToken);
        if (body.IsFailure)
        {
            return Result.Failure<JsonElement>(body.Error);
        }

        Result<string> content = OpenRouterJson.ExtractContent(body.Value);
        if (content.IsFailure)
        {
            return Result.Failure<JsonElement>(content.Error);
        }

        try
        {
            using var document = JsonDocument.Parse(content.Value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Result.Failure<JsonElement>(Error.Failure("Ai.InvalidResponse", "Răspunsul AI nu e JSON valid."));
        }
    }

    internal static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    internal static decimal? Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) => parsed,
            _ => null,
        };
    }

    internal static DateOnly? Date(JsonElement root, string name) =>
        Text(root, name) is { Length: >= 10 } text && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : null;
}
