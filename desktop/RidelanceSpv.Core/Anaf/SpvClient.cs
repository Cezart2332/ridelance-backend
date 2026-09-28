using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace RidelanceSpv.Core.Anaf;

/// <summary>Un mesaj din <c>listaMesaje</c>, cu data convertită în UTC.</summary>
public sealed record SpvListedMessage(string Id, string Cif, string Type, DateTime CreatedAtUtc, string? RequestId, string? Details);

/// <summary>Documentul unui mesaj (de obicei PDF).</summary>
public sealed record SpvDocument(string FileName, string ContentType, byte[] Content);

/// <summary>O eroare de la ANAF (câmpul <c>eroare</c>) sau de conexiune; mesajul e pentru utilizator.</summary>
public sealed class SpvException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>SPVWS2: listaMesaje, descarcare, cerere.</summary>
public interface ISpvApi
{
    Task<IReadOnlyList<SpvListedMessage>> ListMessagesAsync(int days, CancellationToken cancellationToken);

    Task<SpvDocument> DownloadAsync(string messageId, CancellationToken cancellationToken);

    /// <summary>Trimite o cerere; întoarce <c>id_solicitare</c>.</summary>
    Task<string> RequestAsync(string type, string cui, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken);
}

/// <summary>
/// Serviciul web SPV al ANAF (<c>webserviced.anaf.ro/SPVWS2/rest</c>). Autentificarea e chiar
/// conexiunea TLS cu certificatul calificat de pe stick: Windows cere PIN-ul prin driverul
/// stickului, iar cheia privată nu iese niciodată de pe el.
/// </summary>
public sealed class SpvClient : ISpvApi, IDisposable
{
    public const string DefaultBaseUrl = "https://webserviced.anaf.ro/SPVWS2/rest";

    private static readonly TimeZoneInfo Bucharest = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "GTB Standard Time" : "Europe/Bucharest");

    private static readonly string[] DateFormats =
    [
        "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "yyyyMMddHHmm", "yyyyMMddHHmmss", "yyyy-MM-dd HH:mm:ss", "dd.MM.yyyy",
    ];

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public SpvClient(X509Certificate2 certificate, string baseUrl = DefaultBaseUrl)
    {
        var handler = new HttpClientHandler { ClientCertificateOptions = ClientCertificateOption.Manual };
        handler.ClientCertificates.Add(certificate);
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        _baseUrl = baseUrl.TrimEnd('/');
    }

    internal SpvClient(HttpClient http, string baseUrl = DefaultBaseUrl)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<IReadOnlyList<SpvListedMessage>> ListMessagesAsync(int days, CancellationToken cancellationToken)
    {
        byte[] body = await GetAsync($"{_baseUrl}/listaMesaje?zile={Math.Clamp(days, 1, 60).ToString(CultureInfo.InvariantCulture)}", cancellationToken);
        using JsonDocument json = Parse(body);
        JsonElement root = json.RootElement;
        if (Text(root, "eroare") is { } error)
        {
            // „Nu exista mesaje…”: listă goală, nu eroare.
            return error.Contains("Nu exista mesaje", StringComparison.OrdinalIgnoreCase) ? [] : throw new SpvException($"ANAF: {error}");
        }

        var messages = new List<SpvListedMessage>();
        if (root.TryGetProperty("mesaje", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (Text(item, "id") is { } id)
                {
                    messages.Add(new SpvListedMessage(
                        id,
                        Text(item, "cif") ?? string.Empty,
                        Text(item, "tip") ?? string.Empty,
                        Time(Text(item, "data_creare")),
                        Text(item, "id_solicitare"),
                        Text(item, "detalii")));
                }
            }
        }

        return messages;
    }

    public async Task<SpvDocument> DownloadAsync(string messageId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/descarcare?id={Uri.EscapeDataString(messageId)}");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        string contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || content is [(byte)'{', ..])
        {
            using JsonDocument json = Parse(content);
            throw new SpvException($"ANAF: {Text(json.RootElement, "eroare") ?? "documentul nu a putut fi descărcat."}");
        }

        string fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"{messageId}{(contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ? ".pdf" : ".bin")}";
        return new SpvDocument(fileName, contentType, content);
    }

    public async Task<string> RequestAsync(string type, string cui, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        string query = string.Join(
            "&",
            new[] { ("tip", type), ("cui", cui) }
                .Concat(parameters.Select(pair => (pair.Key, pair.Value)))
                .Select(pair => $"{Uri.EscapeDataString(pair.Item1)}={Uri.EscapeDataString(pair.Item2)}"));
        byte[] body = await GetAsync($"{_baseUrl}/cerere?{query}", cancellationToken);
        using JsonDocument json = Parse(body);
        return Text(json.RootElement, "id_solicitare")
            ?? throw new SpvException($"ANAF: {Text(json.RootElement, "eroare") ?? "cererea nu a fost primită."}");
    }

    public void Dispose() => _http.Dispose();

    private async Task<byte[]> GetAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new SpvException("Nu mă pot conecta la SPV. Verifică stickul, PIN-ul și conexiunea la internet.", exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            int status = (int)response.StatusCode;
            response.Dispose();
            throw new SpvException(status is 401 or 403
                ? "SPV a refuzat certificatul. Verifică că stickul e cel înregistrat în SPV."
                : $"SPV a răspuns cu HTTP {status}.");
        }

        return response;
    }

    private static JsonDocument Parse(byte[] body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new SpvException("Răspuns neașteptat de la SPV.", exception);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    /// <summary>Data ANAF e ora României; formatul diferă între servicii, deci se încearcă mai multe.</summary>
    internal static DateTime Time(string? value) =>
        DateTime.TryParseExact(value?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local)
            ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Bucharest)
            : DateTime.UtcNow;
}
