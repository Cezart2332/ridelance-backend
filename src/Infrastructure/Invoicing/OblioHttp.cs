using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Services;

namespace Infrastructure.Invoicing;

/// <summary>Credențialele unui cont Oblio. Platforma are unele, fiecare proprietar are altele.</summary>
internal sealed record OblioCredentials(string ClientId, string ClientSecret, string Cif, string BaseUrl);

/// <summary>
/// Mecanica de transport către API-ul Oblio: token, cereri, citirea câmpului <c>data</c>, erori.
/// </summary>
/// <remarks>
/// Extrasă din <see cref="OblioService"/> ca să poată fi folosită și cu credențialele unui
/// proprietar, nu doar cu cele ale platformei din configurare.
///
/// Cache-ul de token e cheiat pe <c>client_id</c>. Înainte era un singur token static pentru tot
/// procesul, ceea ce mergea cât timp exista un singur cont; cu credențiale per proprietar,
/// aceeași variabilă ar fi servit token-ul unei firme cererilor alteia.
/// </remarks>
internal static class OblioHttp
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record CachedToken(string Token, DateTime ExpiresUtc);

    private static readonly ConcurrentDictionary<string, CachedToken> Tokens = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public static Uri BuildUri(OblioCredentials credentials, string path) =>
        new($"{credentials.BaseUrl.TrimEnd('/')}/{path}");

    public static async Task<string> GetAccessTokenAsync(
        HttpClient httpClient,
        OblioCredentials credentials,
        CancellationToken ct)
    {
        if (Tokens.TryGetValue(credentials.ClientId, out CachedToken? cached) && DateTime.UtcNow < cached.ExpiresUtc)
        {
            return cached.Token;
        }

        await Lock.WaitAsync(ct);
        try
        {
            if (Tokens.TryGetValue(credentials.ClientId, out cached) && DateTime.UtcNow < cached.ExpiresUtc)
            {
                return cached.Token;
            }

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret,
            });

            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsync(BuildUri(credentials, "authorize/token"), content, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new OblioApiException("Nu s-a putut contacta API-ul Oblio.", ex);
            }

            string payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new OblioApiException(
                    $"Autentificarea Oblio a eșuat ({(int)response.StatusCode}): {ExtractErrorMessage(payload)}");
            }

            using var doc = JsonDocument.Parse(payload);
            string? token = doc.RootElement.TryGetProperty("access_token", out JsonElement tokenEl)
                ? tokenEl.GetString()
                : null;

            if (string.IsNullOrEmpty(token))
            {
                throw new OblioApiException("Răspunsul de autentificare Oblio nu conține access_token.");
            }

            int expiresIn = 3600;
            if (doc.RootElement.TryGetProperty("expires_in", out JsonElement expEl))
            {
                _ = int.TryParse(expEl.ToString(), out expiresIn);
            }

            // Un minut de margine: un token care expiră între verificare și cerere ar fi produs
            // un 401 pe care nimeni nu l-ar fi putut explica.
            Tokens[credentials.ClientId] = new CachedToken(token, DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60)));
            return token;
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Uită token-ul unui cont — după deconectare sau după schimbarea secretului.</summary>
    public static void ForgetToken(string clientId) => Tokens.TryRemove(clientId, out _);

    public static async Task<JsonElement> GetAsync(
        HttpClient httpClient,
        OblioCredentials credentials,
        string path,
        CancellationToken ct)
    {
        string token = await GetAccessTokenAsync(httpClient, credentials, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(credentials, path));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await SendAndReadDataAsync(httpClient, request, ct);
    }

    public static async Task<JsonElement> SendJsonAsync(
        HttpClient httpClient,
        OblioCredentials credentials,
        HttpMethod method,
        string path,
        object body,
        CancellationToken ct)
    {
        string token = await GetAccessTokenAsync(httpClient, credentials, ct);
        using var request = new HttpRequestMessage(method, BuildUri(credentials, path));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        return await SendAndReadDataAsync(httpClient, request, ct);
    }

    public static async Task<JsonElement> SendAndReadDataAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new OblioApiException("Nu s-a putut contacta API-ul Oblio.", ex);
        }

        string payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new OblioApiException(
                $"Cererea către Oblio a eșuat ({(int)response.StatusCode}): {ExtractErrorMessage(payload)}");
        }

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data))
        {
            throw new OblioApiException("Răspunsul Oblio nu conține câmpul \"data\".");
        }

        return data.Clone();
    }

    public static string? NormalizeCif(string? cif) =>
        cif?.Replace("RO", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

    public static string ExtractErrorMessage(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("statusMessage", out JsonElement message))
            {
                return message.GetString() ?? payload;
            }
        }
        catch (JsonException)
        {
            // fall through — return raw payload
        }

        return payload.Length > 500 ? payload[..500] : payload;
    }
}
