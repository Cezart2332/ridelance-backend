using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Ai;
using Application.Documents.AiVerification;
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

        string fieldsInstruction = request.Fields.Count == 0
            ? "Nu extrage câmpuri de business suplimentare."
            : "Extrage următoarele câmpuri de business în „fields” (per câmp: value = textul exact sau null dacă lipsește, confidence = 0..1):\n" +
              string.Join("\n", request.Fields.Select(f =>
                  $"- {f.Key} ({f.Type}): {f.Description}{(f.Required ? " [obligatoriu]" : string.Empty)}"));

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
                                $"{fieldsInstruction}\n" +
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

    /// <summary>
    /// Promptul cere <b>doar extragere</b>. Modelul nu primește data curentă și nu decide dacă
    /// documentul e valabil sau expirat: nu are ceas, iar când îl puneam să judece respingea acte
    /// bune pentru că „data eliberării e în viitor". Comparațiile temporale se fac în C#
    /// (<c>DocumentDateValidator</c>), unde sunt deterministe și testabile.
    /// </summary>
    private static string BuildSystemPrompt() =>
        "Ești un extractor de date din documente pentru RIDElance, o platformă pentru șoferi de ridesharing din România. " +
        "Primești un document încărcat de un client, împreună cu tipul de document așteptat. " +
        "Sarcina ta este STRICT de citire și extragere. Analizează vizual documentul (folosește capacitățile tale " +
        "native de citire a textului din imagine/PDF) și raportează: " +
        "1) dacă documentul corespunde tipului așteptat (nu alt tip de document, nu o poză goală sau fără legătură); " +
        "2) dacă este lizibil și complet (nu tăiat, nu prea neclar pentru a fi citit); " +
        "3) data eliberării/emiterii, dacă apare pe document; " +
        "4) data expirării/valabilității, dacă apare pe document. " +
        "NU evalua dacă documentul este expirat, valabil, recent sau vechi. NU compara datele de pe document cu " +
        "prezentul — nu cunoști data curentă, iar acest lucru se verifică ulterior, în afara ta. " +
        "Raportează datele exact cum apar pe document, fără să le corectezi și fără să le respingi ca improbabile. " +
        "Toate datele se normalizează în format ISO 8601, adică YYYY-MM-DD (exemplu: 15.09.2025 devine 2025-09-15). " +
        "Dacă o dată nu apare pe document sau nu poate fi citită cu certitudine, întoarce null pentru ea — nu ghici. " +
        "NU include în răspuns date personale sensibile (CNP, serie și număr de act). " +
        "Răspunde STRICT cu un obiect JSON valid, fără niciun alt text, în exact acest format: " +
        "{\"matches_expected_type\": boolean, \"readable\": boolean, " +
        "\"issued_on\": \"YYYY-MM-DD\" sau null, \"expires_at\": \"YYYY-MM-DD\" sau null, " +
        "\"detected_type\": \"ce document este de fapt, pe scurt\", " +
        "\"reason\": \"explicație scurtă în română, max 200 de caractere, pe înțelesul clientului\", " +
        "\"overall_confidence\": number între 0 și 1, " +
        "\"fields\": { \"cheie_camp\": {\"value\": \"text sau null\", \"confidence\": number între 0 și 1}, ... }}. " +
        "Extrage în \"fields\" DOAR câmpurile cerute în mesajul utilizatorului; dacă nu sunt cerute câmpuri, întoarce \"fields\": {}. " +
        "NU pune NICIODATĂ în \"fields\" CNP, serie sau număr de act de identitate. " +
        "Dacă ai dubii rezonabile despre tip sau lizibilitate, preferă valorile permisive și explică în " +
        "\"reason\" — verificarea finală o face un om.";

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
            string detectedType = GetString(root, "detected_type") ?? string.Empty;
            string reason = GetString(root, "reason") ?? string.Empty;

            // Parsarea e tolerantă la format: cerem ISO, dar modelul mai întoarce și forma de pe
            // document. Ce nu e o dată reală rămâne null și se tratează ca „nu s-a putut citi".
            DateOnly? issuedOn = DocumentDateValidator.Parse(GetString(root, "issued_on"));
            DateOnly? expiresAt = DocumentDateValidator.Parse(GetString(root, "expires_at"));

            double overallConfidence = GetDouble(root, "overall_confidence") ?? 0d;
            IReadOnlyList<AiFieldResult> fields = ParseFields(root);

            return new DocumentAiAnalysisResult(
                matches, readable, issuedOn, expiresAt, detectedType, reason, fields, overallConfidence);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Răspunsul OpenRouter nu a putut fi interpretat.");
            return Result.Failure<DocumentAiAnalysisResult>(Error.Failure(
                "Ai.InvalidResponse",
                "Răspunsul serviciului AI nu a putut fi interpretat."));
        }
    }

    private static List<AiFieldResult> ParseFields(JsonElement root)
    {
        if (!root.TryGetProperty("fields", out JsonElement fields) || fields.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var results = new List<AiFieldResult>();
        foreach (JsonProperty field in fields.EnumerateObject())
        {
            string key = field.Name;
            string? value = null;
            double confidence = 0d;

            if (field.Value.ValueKind == JsonValueKind.Object)
            {
                value = GetString(field.Value, "value");
                confidence = GetDouble(field.Value, "confidence") ?? 0d;
            }
            else if (field.Value.ValueKind == JsonValueKind.String)
            {
                // Model tolerant: uneori întoarce direct valoarea, fără obiect.
                value = field.Value.GetString();
            }

            results.Add(new AiFieldResult(key, value, Math.Clamp(confidence, 0d, 1d)));
        }

        return results;
    }

    private static double? GetDouble(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement element) && element.ValueKind == JsonValueKind.Number
            ? element.GetDouble()
            : null;

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
