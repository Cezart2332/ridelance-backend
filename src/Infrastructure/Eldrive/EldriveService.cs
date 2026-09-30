using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Eldrive;

/// <summary>
/// <c>POST partner-invites/v2.0</c> invită o adresă în contul de partener RIDElance;
/// <c>DELETE partner-invites/v2.0/{id}</c> o scoate.
/// </summary>
internal sealed class EldriveService(
    HttpClient httpClient,
    IOptions<EldriveOptions> options,
    ILogger<EldriveService> logger) : IEldriveService
{
    private const string InvitesPath = "partner-invites/v2.0";

    private static readonly Error NotConfigured = Error.Problem(
        "Eldrive.NotConfigured", "Conexiunea Eldrive nu e configurată încă. Revino puțin mai târziu.");

    private static readonly Error Unavailable = Error.Problem(
        "Eldrive.Unavailable", "Eldrive nu a răspuns. Încearcă din nou în câteva minute.");

    private readonly EldriveOptions _options = options.Value;

    public async Task<Result<EldriveInviteResult>> InviteAsync(string email, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            logger.LogWarning("Eldrive neconfigurat: invitația nu a plecat.");
            return Result.Failure<EldriveInviteResult>(NotConfigured);
        }

        using HttpRequestMessage request = Request(HttpMethod.Post, InvitesPath);
        request.Content = JsonContent.Create(new { partnerId = _options.PartnerId, sendViaEmail = true, email });

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Eldrive a refuzat invitația: {Status} {Body}", (int)response.StatusCode, Truncate(body));
                return Result.Failure<EldriveInviteResult>(Error.Problem(
                    "Eldrive.InviteRejected",
                    ReadMessage(body) is { } message
                        ? $"Eldrive a refuzat invitația: {message}"
                        : "Eldrive a refuzat invitația. Verifică adresa de email."));
            }

            using var document = JsonDocument.Parse(body);
            JsonElement data = document.RootElement.GetProperty("data");
            return new EldriveInviteResult(
                data.GetProperty("id").GetInt64(),
                data.TryGetProperty("status", out JsonElement status) ? status.GetString() ?? string.Empty : string.Empty,
                data.TryGetProperty("userId", out JsonElement userId) && userId.ValueKind == JsonValueKind.Number
                    ? userId.GetInt64()
                    : null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogError(exception, "Invitația Eldrive a eșuat.");
            return Result.Failure<EldriveInviteResult>(Unavailable);
        }
    }

    public async Task<Result> DeleteInviteAsync(long inviteId, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return Result.Failure(NotConfigured);
        }

        using HttpRequestMessage request = Request(HttpMethod.Delete, $"{InvitesPath}/{inviteId}");

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Success();
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Eldrive a refuzat ștergerea invitației {InviteId}: {Status} {Body}", inviteId, (int)response.StatusCode, Truncate(body));
            return Result.Failure(Error.Problem(
                "Eldrive.DeleteRejected",
                ReadMessage(body) is { } message ? $"Eldrive a refuzat ștergerea: {message}" : "Eldrive a refuzat ștergerea invitației."));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "Ștergerea invitației Eldrive {InviteId} a eșuat.", inviteId);
            return Result.Failure(Unavailable);
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(_options.BaseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>Mesajul de eroare al Eldrive, dacă răspunsul are unul într-o formă cunoscută.</summary>
    private static string? ReadMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
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

    private static string Truncate(string body) => body.Length <= 500 ? body : body[..500];
}
