using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Ai;

internal sealed class OpenRouterDocumentAiAnalyzer(
    HttpClient httpClient,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterDocumentAiAnalyzer> logger) : IDocumentAiAnalyzer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<Result<DocumentAiAnalysisResult>> AnalyzeAsync(
        DocumentAiAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        OpenRouterOptions config = options.Value;

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return Result.Failure<DocumentAiAnalysisResult>(Error.Failure(
                "Ai.NotConfigured",
                "Cheia API OpenRouter nu este configurată."));
        }

        bool isPdf = request.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase);
        string dataUrl = $"data:{request.ContentType};base64,{Convert.ToBase64String(request.FileBytes)}";

        object filePart = isPdf
            ? new { type = "file", file = new { filename = request.FileName, file_data = dataUrl } }
            : new { type = "image_url", image_url = new { url = dataUrl } };

        string expiryInstruction = request.ExpectsExpiryDate
            ? "Documentul ar trebui să aibă o dată de expirare / valabilitate — extrage-o obligatoriu dacă este vizibilă."
            : "Dacă documentul are totuși o dată de expirare / valabilitate, extrage-o.";

        var payload = new
        {
            model = config.Model,
            temperature = 0,
            // Pentru PDF folosim OCR-ul nativ al modelului (Gemini), nu parserul extern OpenRouter.
            plugins = isPdf
                ? new object[] { new { id = "file-parser", pdf = new { engine = "native" } } }
                : null,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = BuildSystemPrompt(),
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text =
                                $"Tip de document așteptat: {request.ExpectedDocumentLabel}.\n" +
                                $"Descriere: {request.ExpectationDetails}\n" +
                                $"{expiryInstruction}\n" +
                                $"Nume fișier încărcat: {request.FileName}",
                        },
                        filePart,
                    },
                },
            },
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{config.BaseUrl.TrimEnd('/')}/chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        httpRequest.Headers.Add("HTTP-Referer", "https://ridelance.ro");
        httpRequest.Headers.Add("X-Title", "RIDElance");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, SerializerOptions),
            Encoding.UTF8,
            "application/json");

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(httpRequest, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "OpenRouter a răspuns cu {StatusCode} pentru fișierul {FileName}.",
                    (int)response.StatusCode,
                    request.FileName);
                return Result.Failure<DocumentAiAnalysisResult>(Error.Failure(
                    "Ai.RequestFailed",
                    $"OpenRouter a răspuns cu status {(int)response.StatusCode}."));
            }

            return ParseResponse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                   !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Apelul către OpenRouter a eșuat pentru fișierul {FileName}.", request.FileName);
            return Result.Failure<DocumentAiAnalysisResult>(Error.Failure(
                "Ai.RequestFailed",
                "Apelul către serviciul AI a eșuat."));
        }
    }

    private static string BuildSystemPrompt() =>
        "Ești un verificator de documente pentru RIDElance, o platformă pentru șoferi de ridesharing din România. " +
        "Primești un document încărcat de un client în procesul de onboarding, împreună cu tipul de document așteptat. " +
        "Analizează vizual documentul (folosește capacitățile tale native de citire a textului din imagine/PDF) și stabilește: " +
        "1) dacă documentul corespunde tipului așteptat (nu alt tip de document, nu o poză goală sau fără legătură); " +
        "2) dacă este lizibil și complet (nu tăiat, nu prea neclar pentru a fi verificat); " +
        "3) dacă nu este expirat, raportat la data de azi " +
        DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
        "; 4) data de expirare/valabilitate, dacă există. " +
        "NU include în răspuns date personale sensibile (CNP, serie și număr de act). " +
        "Răspunde STRICT cu un obiect JSON valid, fără niciun alt text, în exact acest format: " +
        "{\"matches_expected_type\": boolean, \"readable\": boolean, \"expired\": boolean sau null dacă nu se aplică, " +
        "\"expires_at\": \"YYYY-MM-DD\" sau null, \"detected_type\": \"ce document este de fapt, pe scurt\", " +
        "\"valid\": boolean, \"reason\": \"explicație scurtă în română, max 200 de caractere, pe înțelesul clientului\"}. " +
        "Câmpul \"valid\" este true doar dacă documentul corespunde tipului așteptat, este lizibil și nu este expirat. " +
        "Dacă ai dubii rezonabile, preferă \"valid\": true și explică în \"reason\" — verificarea finală o face un om.";

    private Result<DocumentAiAnalysisResult> ParseResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            string? content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                return Result.Failure<DocumentAiAnalysisResult>(Error.Failure(
                    "Ai.EmptyResponse",
                    "Serviciul AI a returnat un răspuns gol."));
            }

            string json = StripCodeFences(content);

            using var verdict = JsonDocument.Parse(json);
            JsonElement root = verdict.RootElement;

            bool matches = GetBool(root, "matches_expected_type") ?? false;
            bool readable = GetBool(root, "readable") ?? false;
            bool valid = GetBool(root, "valid") ?? false;
            bool? expired = GetBool(root, "expired");
            string detectedType = GetString(root, "detected_type") ?? string.Empty;
            string reason = GetString(root, "reason") ?? string.Empty;

            DateOnly? expiresAt = null;
            string? expiresRaw = GetString(root, "expires_at");
            if (!string.IsNullOrWhiteSpace(expiresRaw) &&
                DateOnly.TryParseExact(expiresRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
            {
                expiresAt = parsed;
            }

            return new DocumentAiAnalysisResult(matches, readable, valid, expired, expiresAt, detectedType, reason);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Răspunsul OpenRouter nu a putut fi interpretat.");
            return Result.Failure<DocumentAiAnalysisResult>(Error.Failure(
                "Ai.InvalidResponse",
                "Răspunsul serviciului AI nu a putut fi interpretat."));
        }
    }

    private static bool? GetBool(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static string StripCodeFences(string content)
    {
        string trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        int firstNewline = trimmed.IndexOf('\n', StringComparison.Ordinal);
        if (firstNewline < 0)
        {
            return trimmed;
        }

        trimmed = trimmed[(firstNewline + 1)..];
        int closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return closingFence >= 0 ? trimmed[..closingFence].Trim() : trimmed.Trim();
    }
}
