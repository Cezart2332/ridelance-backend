using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.Ai;

/// <summary>
/// Partea comună a apelurilor către OpenRouter: trimiterea cererii și scoaterea JSON-ului din
/// răspuns.
/// </summary>
/// <remarks>
/// Extrasă când a apărut al doilea consumator (generatorul de text, lângă analizorul de
/// documente). Erau aceleași patruzeci de linii de două ori — inclusiv detalii ușor de uitat la
/// copiere, cum e faptul că modelele întorc uneori JSON-ul învelit în ```-uri, deși li s-a cerut
/// obiect curat.
/// </remarks>
internal static class OpenRouterJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Trimite un payload de chat completions și întoarce corpul brut al răspunsului.</summary>
    public static async Task<Result<string>> SendAsync(
        HttpClient httpClient,
        OpenRouterOptions config,
        object payload,
        ILogger logger,
        string context,
        CancellationToken cancellationToken)
    {
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
                    "OpenRouter a răspuns cu {StatusCode} pentru {Context}.",
                    (int)response.StatusCode,
                    context);
                return Result.Failure<string>(Error.Failure(
                    "Ai.RequestFailed",
                    $"OpenRouter a răspuns cu status {(int)response.StatusCode}."));
            }

            return Result.Success(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                   !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Apelul către OpenRouter a eșuat pentru {Context}.", context);
            return Result.Failure<string>(Error.Failure(
                "Ai.RequestFailed",
                "Apelul către serviciul AI a eșuat."));
        }
    }

    /// <summary>Scoate conținutul primului mesaj din răspuns, curățat de garduri de cod.</summary>
    public static Result<string> ExtractContent(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            string? content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content)
                ? Result.Failure<string>(Error.Failure("Ai.EmptyResponse", "Serviciul AI a returnat un răspuns gol."))
                : Result.Success(StripCodeFences(content));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            return Result.Failure<string>(Error.Failure(
                "Ai.InvalidResponse",
                "Răspunsul serviciului AI nu a putut fi interpretat."));
        }
    }

    /// <summary>
    /// Scoate ```-urile în care unele modele învelesc JSON-ul, chiar și cu <c>json_object</c> cerut.
    /// </summary>
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
