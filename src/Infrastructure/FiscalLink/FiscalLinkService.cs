using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.FiscalLink;

/// <summary>
/// Gestiunea la FiscalLink, cu cheia de gestiune în <c>X-Api-Key</c>: <c>POST clients</c>,
/// <c>GET clients/{id}/activation</c>, <c>GET registers?clientId=</c>.
/// </summary>
internal sealed class FiscalLinkService(
    HttpClient httpClient,
    IOptions<FiscalLinkOptions> options,
    ILogger<FiscalLinkService> logger) : IFiscalLinkService
{
    private static readonly Error NotConfigured = Error.Problem(
        "FiscalLink.NotConfigured", "Conexiunea FiscalLink nu e configurată încă. Revino puțin mai târziu.");

    private static readonly Error Unavailable = Error.Problem(
        "FiscalLink.Unavailable", "FiscalLink nu a răspuns. Încearcă din nou în câteva minute.");

    private readonly FiscalLinkOptions _options = options.Value;

    public async Task<Result<Guid>> CreateClientAsync(FiscalLinkNewClient client, CancellationToken cancellationToken = default)
    {
        Result<JsonElement> response = await SendAsync(HttpMethod.Post, "clients", client, "crearea clientului", cancellationToken);
        return response.IsSuccess
            ? Result.Success(response.Value.GetProperty("id").GetGuid())
            : Result.Failure<Guid>(response.Error);
    }

    public async Task<Result<FiscalLinkActivation>> GetActivationAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        Result<JsonElement> response = await SendAsync(HttpMethod.Get, $"clients/{clientId}/activation", null, "codul de activare", cancellationToken);
        if (response.IsFailure)
        {
            return Result.Failure<FiscalLinkActivation>(response.Error);
        }

        JsonElement body = response.Value;
        return new FiscalLinkActivation(
            String(body, "activationCode") ?? string.Empty,
            String(body, "activationLink") ?? string.Empty,
            body.TryGetProperty("registerCount", out JsonElement count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : 0);
    }

    public async Task<Result<IReadOnlyList<FiscalLinkRegister>>> ListRegistersAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        Result<JsonElement> response = await SendAsync(
            HttpMethod.Get, $"registers?clientId={clientId}&page=1&pageSize=100", null, "casele de marcat", cancellationToken);
        if (response.IsFailure)
        {
            return Result.Failure<IReadOnlyList<FiscalLinkRegister>>(response.Error);
        }

        return Result.Success<IReadOnlyList<FiscalLinkRegister>>([.. Items(response.Value).Select(ReadRegister)]);
    }

    /// <summary>
    /// O cerere cu cheia de gestiune. Întoarce corpul JSON sau o eroare pe care o poate citi omul:
    /// mesajul FiscalLink când îl are, altfel unul generic.
    /// </summary>
    private async Task<Result<JsonElement>> SendAsync(
        HttpMethod method, string path, object? body, string operation, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            logger.LogWarning("FiscalLink neconfigurat: {Operation} nu a plecat.", operation);
            return Result.Failure<JsonElement>(NotConfigured);
        }

        using var request = new HttpRequestMessage(method, new Uri(_options.BaseUrl, path));
        request.Headers.Add("X-Api-Key", _options.ManagementKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonSerializerOptions.Web);
        }

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string text = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("{Operation}: FiscalLink a răspuns {Status} {Body}", operation, (int)response.StatusCode, text.Length <= 500 ? text : text[..500]);
                return Result.Failure<JsonElement>(Error.Problem(
                    "FiscalLink.Rejected",
                    ReadMessage(text) is { } message ? $"FiscalLink: {message}" : $"FiscalLink a refuzat cererea ({operation}, cod {(int)response.StatusCode})."));
            }

            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(exception, "{Operation} a eșuat.", operation);
            return Result.Failure<JsonElement>(Unavailable);
        }
    }

    /// <summary>Elementele unei liste paginate: fie un array, fie un obiect cu lista sub un nume obișnuit.</summary>
    private static List<JsonElement> Items(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return [.. root.EnumerateArray()];
        }

        foreach (string name in new[] { "items", "data", "results", "registers" })
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(name, out JsonElement list)
                && list.ValueKind == JsonValueKind.Array)
            {
                return [.. list.EnumerateArray()];
            }
        }

        return [];
    }

    private static FiscalLinkRegister ReadRegister(JsonElement register) => new(
        register.GetProperty("id").GetGuid(),
        String(register, "serialNumber"),
        String(register, "status") ?? string.Empty,
        Bool(register, "isOnline"),
        Bool(register, "awaitingClientConsent"),
        String(register, "activatedAtUtc") is { } activated
            && DateTime.TryParse(activated, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at)
                ? at
                : null);

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static string? ReadMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            // Unele erori vin ca un simplu string JSON: "No register with this serial on …".
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return string.IsNullOrWhiteSpace(document.RootElement.GetString()) ? null : document.RootElement.GetString();
            }

            foreach (string name in new[] { "message", "detail", "title", "error" })
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty(name, out JsonElement value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Răspuns fără JSON: rămâne mesajul generic.
        }

        return null;
    }
}
