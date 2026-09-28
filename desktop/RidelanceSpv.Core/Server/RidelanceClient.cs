using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RidelanceSpv.Core.Anaf;

namespace RidelanceSpv.Core.Server;

public sealed record SpvRequestToSend(Guid Id, string Type, string Cui, Dictionary<string, string> Parameters);

/// <summary>Ce dă serverul la pornirea unei trimiteri.</summary>
public sealed record SpvRunStart(Guid RunId, int Days, List<string> Cuis, List<SpvRequestToSend> Requests);

public sealed record SpvAgentStatus(string KeyName, int Pfas, DateTime? LastSuccessAtUtc, int QueuedRequests);

/// <summary>O eroare de la serverul RIDElance, cu codul ei (<c>Spv.RunInProgress</c>).</summary>
public sealed class RidelanceException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>Serverul RIDElance, văzut de aplicație (<c>/spv/agent/…</c>, cu cheia în <c>X-Agent-Key</c>).</summary>
public interface IRidelanceApi
{
    Task<SpvAgentStatus> StatusAsync(CancellationToken cancellationToken);

    Task<SpvRunStart> StartRunAsync(string machine, string agentVersion, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> NewIdsAsync(Guid runId, IReadOnlyList<string> ids, CancellationToken cancellationToken);

    Task SendMessageAsync(Guid runId, SpvListedMessage message, SpvDocument? document, CancellationToken cancellationToken);

    Task ReportRequestAsync(Guid runId, Guid requestId, string? anafRequestId, string? error, CancellationToken cancellationToken);

    Task FinishRunAsync(Guid runId, string? error, CancellationToken cancellationToken);
}

public sealed class RidelanceClient : IRidelanceApi, IDisposable
{
    public const string DefaultBaseUrl = "https://api.ridelance.ro";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public RidelanceClient(string baseUrl, string key)
        : this(new HttpClient(), baseUrl, key)
    {
    }

    internal RidelanceClient(HttpClient http, string baseUrl, string key)
    {
        _http = http;
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Add("X-Agent-Key", key);
    }

    public Task<SpvAgentStatus> StatusAsync(CancellationToken cancellationToken) =>
        SendAsync<SpvAgentStatus>(new HttpRequestMessage(HttpMethod.Get, "spv/agent/status"), cancellationToken);

    public Task<SpvRunStart> StartRunAsync(string machine, string agentVersion, CancellationToken cancellationToken) =>
        SendAsync<SpvRunStart>(
            new HttpRequestMessage(HttpMethod.Post, "spv/agent/runs") { Content = JsonContent.Create(new { machine, agentVersion }, options: Json) },
            cancellationToken);

    public async Task<IReadOnlyList<string>> NewIdsAsync(Guid runId, IReadOnlyList<string> ids, CancellationToken cancellationToken) =>
        await SendAsync<List<string>>(
            new HttpRequestMessage(HttpMethod.Post, $"spv/agent/runs/{runId}/new-ids") { Content = JsonContent.Create(new { ids }, options: Json) },
            cancellationToken);

    public async Task SendMessageAsync(Guid runId, SpvListedMessage message, SpvDocument? document, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(message.Id), "id" },
            { new StringContent(message.Cif), "cif" },
            { new StringContent(message.Type), "tip" },
            { new StringContent(message.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)), "dataCreare" },
        };
        if (message.RequestId is { } requestId)
        {
            form.Add(new StringContent(requestId), "idSolicitare");
        }

        if (message.Details is { } details)
        {
            form.Add(new StringContent(details), "detalii");
        }

        if (document is not null)
        {
            var file = new ByteArrayContent(document.Content);
            file.Headers.ContentType = MediaTypeHeaderValue.TryParse(document.ContentType, out MediaTypeHeaderValue? type) ? type : new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", document.FileName);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"spv/agent/runs/{runId}/messages") { Content = form };
        await SendAsync(request, cancellationToken);
    }

    public async Task ReportRequestAsync(Guid runId, Guid requestId, string? anafRequestId, string? error, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"spv/agent/runs/{runId}/requests/{requestId}")
        {
            Content = JsonContent.Create(new { anafRequestId, error }, options: Json),
        };
        await SendAsync(request, cancellationToken);
    }

    public async Task FinishRunAsync(Guid runId, string? error, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"spv/agent/runs/{runId}/finish") { Content = JsonContent.Create(new { error }, options: Json) };
        await SendAsync(request, cancellationToken);
    }

    public void Dispose() => _http.Dispose();

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            using HttpResponseMessage response = await SendAsync(request, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                ?? throw new RidelanceException("Empty", "Serverul RIDElance a răspuns gol.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new RidelanceException("Network", "Serverul RIDElance nu răspunde. Verifică conexiunea la internet.", exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        // ProblemDetails: `title` = codul (Spv.InvalidKey), `detail` = mesajul.
        string code = "Http";
        string message = $"Serverul RIDElance a răspuns cu HTTP {(int)response.StatusCode}.";
        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (problem.RootElement.TryGetProperty("title", out JsonElement title) && title.GetString() is { Length: > 0 } t)
            {
                code = t;
            }

            if (problem.RootElement.TryGetProperty("detail", out JsonElement detail) && detail.GetString() is { Length: > 0 } d)
            {
                message = d;
            }
        }
        catch (JsonException)
        {
            // Nu e ProblemDetails; rămâne mesajul general.
        }

        response.Dispose();
        throw new RidelanceException(code, message);
    }
}
