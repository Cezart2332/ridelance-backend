using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Anaf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Accounting.Anaf;

/// <summary>Aplicația RIDElance înregistrată la ANAF (profil OAuth, serviciul E-Factura). Secțiunea <c>AnafOAuth</c>.</summary>
public sealed class AnafOAuthOptions
{
    public const string SectionName = "AnafOAuth";

    public string ClientId { get; init; } = string.Empty;

    /// <summary>Secretul aplicației: doar din variabile de mediu (<c>AnafOAuth__ClientSecret</c>), niciodată în repo.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>Exact ca la înregistrarea aplicației; ANAF îl compară caracter cu caracter.</summary>
    public string RedirectUri { get; init; } = "https://api.ridelance.ro/anaf/oauth/callback";

    public string AuthorizeUrl { get; init; } = "https://logincert.anaf.ro/anaf-oauth2/v1/authorize";

    public string TokenUrl { get; init; } = "https://logincert.anaf.ro/anaf-oauth2/v1/token";

    /// <summary>Serviciile e-Factura cu OAuth; <c>…/test/FCTEL/rest</c> pentru mediul de test.</summary>
    public string EFacturaBaseUrl { get; init; } = "https://api.anaf.ro/prod/FCTEL/rest";

    /// <summary>Transformarea XML → PDF: serviciu public, fără token.</summary>
    public string PdfBaseUrl { get; init; } = "https://webservicesp.anaf.ro/prod/FCTEL/rest";
}

/// <summary>
/// OAuth ANAF (procedura „Oauth_procedura_inregistrare_aplicatii_portal_ANAF”) și e-Factura:
/// <list type="bullet">
/// <item>authorize: <c>response_type=code</c>, <c>token_content_type=jwt</c>, fără scope;</item>
/// <item>token și refresh: Basic auth cu ClientId/ClientSecret, corp <c>x-www-form-urlencoded</c>;
/// la refresh se rotesc ambele tokenuri, deci se salvează amândouă;</item>
/// <item>listă: <c>listaMesajePaginatieFactura</c> (interval în milisecunde, max. 60 de zile);</item>
/// <item>descărcare: <c>descarcare?id=</c>, arhivă ZIP.</item>
/// </list>
/// ANAF întoarce erorile de business cu 200 și un câmp <c>eroare</c>.
/// </summary>
internal sealed class AnafEFacturaClient(HttpClient http, IOptions<AnafOAuthOptions> options, ILogger<AnafEFacturaClient> logger) : IAnafEFacturaClient
{
    private static readonly TimeZoneInfo Bucharest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");

    private AnafOAuthOptions Settings => options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Settings.ClientId) &&
        !string.IsNullOrWhiteSpace(Settings.ClientSecret) &&
        !string.IsNullOrWhiteSpace(Settings.RedirectUri);

    public Uri AuthorizeUrl(string state) => new(
        $"{Settings.AuthorizeUrl}?response_type=code" +
        $"&client_id={Uri.EscapeDataString(Settings.ClientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(Settings.RedirectUri)}" +
        "&token_content_type=jwt" +
        $"&state={Uri.EscapeDataString(state)}");

    public Task<Result<AnafTokens>> ExchangeCodeAsync(string code, CancellationToken cancellationToken) =>
        TokenAsync(
            [
                new("grant_type", "authorization_code"),
                new("code", code),
                new("redirect_uri", Settings.RedirectUri),
                new("token_content_type", "jwt"),
            ],
            cancellationToken);

    public Task<Result<AnafTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        TokenAsync(
            [
                new("grant_type", "refresh_token"),
                new("refresh_token", refreshToken),
                new("token_content_type", "jwt"),
            ],
            cancellationToken);

    public async Task<Result<EFacturaPage>> ListMessagesAsync(string accessToken, string cif, DateTime fromUtc, DateTime toUtc, int page, CancellationToken cancellationToken)
    {
        string url = string.Create(
            CultureInfo.InvariantCulture,
            $"{Settings.EFacturaBaseUrl}/listaMesajePaginatieFactura?startTime={new DateTimeOffset(fromUtc).ToUnixTimeMilliseconds()}&endTime={new DateTimeOffset(toUtc).ToUnixTimeMilliseconds()}&cif={Uri.EscapeDataString(cif)}&pagina={page}");
        Result<byte[]> response = await GetAsync(accessToken, url, cancellationToken);
        if (response.IsFailure)
        {
            return Result.Failure<EFacturaPage>(response.Error);
        }

        using var json = JsonDocument.Parse(response.Value);
        JsonElement root = json.RootElement;
        if (Text(root, "eroare") is { } error)
        {
            // „Nu exista mesaje in intervalul selectat”: nu e o eroare, e o listă goală.
            return error.Contains("Nu exista mesaje", StringComparison.OrdinalIgnoreCase)
                ? new EFacturaPage([], 0, null)
                : new EFacturaPage([], 0, error);
        }

        var messages = new List<EFacturaListItem>();
        if (root.TryGetProperty("mesaje", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (Text(item, "id") is not { } id)
                {
                    continue;
                }

                messages.Add(new EFacturaListItem(
                    id,
                    Text(item, "tip") ?? string.Empty,
                    AnafTime(Text(item, "data_creare")),
                    Text(item, "id_solicitare"),
                    Text(item, "cif"),
                    Text(item, "detalii")));
            }
        }

        int pages = root.TryGetProperty("numar_total_pagini", out JsonElement total) && total.TryGetInt32(out int count) ? count : 1;
        return new EFacturaPage(messages, pages, null);
    }

    public async Task<Result<byte[]>> DownloadAsync(string accessToken, string messageId, CancellationToken cancellationToken)
    {
        Result<byte[]> response = await GetAsync(accessToken, $"{Settings.EFacturaBaseUrl}/descarcare?id={Uri.EscapeDataString(messageId)}", cancellationToken);
        if (response.IsFailure)
        {
            return response;
        }

        // Arhiva începe cu „PK”; altfel ANAF a răspuns cu o eroare JSON.
        return response.Value is [(byte)'P', (byte)'K', ..]
            ? response
            : Result.Failure<byte[]>(AnafErrors.Business(BusinessError(response.Value) ?? "Răspunsul nu e o arhivă ZIP."));
    }

    public async Task<Result<byte[]>> ToPdfAsync(byte[] invoiceXml, bool creditNote, CancellationToken cancellationToken)
    {
        string url = $"{Settings.PdfBaseUrl}/transformare/{(creditNote ? "FCN" : "FACT1")}/DA";
        using var content = new ByteArrayContent(invoiceXml);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
        try
        {
            using HttpResponseMessage response = await http.PostAsync(url, content, cancellationToken);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<byte[]>(AnafErrors.Http(response.StatusCode));
            }

            return body is [(byte)'%', (byte)'P', (byte)'D', (byte)'F', ..]
                ? body
                : Result.Failure<byte[]>(AnafErrors.Business(BusinessError(body) ?? "ANAF nu a generat PDF-ul."));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Transformarea e-Factura în PDF a eșuat");
            return Result.Failure<byte[]>(AnafErrors.Unavailable);
        }
    }

    private async Task<Result<AnafTokens>> TokenAsync(IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return Result.Failure<AnafTokens>(AnafErrors.NotConfigured);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Settings.TokenUrl) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Settings.ClientId}:{Settings.ClientSecret}")));
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Token ANAF refuzat: HTTP {Status}", (int)response.StatusCode);
                return Result.Failure<AnafTokens>(AnafErrors.TokenRejected);
            }

            using var json = JsonDocument.Parse(body);
            string? access = Text(json.RootElement, "access_token");
            string? refresh = Text(json.RootElement, "refresh_token");
            if (access is null || refresh is null)
            {
                return Result.Failure<AnafTokens>(AnafErrors.TokenRejected);
            }

            DateTime now = DateTime.UtcNow;
            return new AnafTokens(access, refresh, JwtExpiry(access) ?? now.AddDays(90), JwtExpiry(refresh) ?? now.AddDays(365));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            logger.LogWarning(exception, "Cererea de token ANAF a eșuat");
            return Result.Failure<AnafTokens>(AnafErrors.Unavailable);
        }
    }

    private async Task<Result<byte[]>> GetAsync(string accessToken, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return response.IsSuccessStatusCode ? body : Result.Failure<byte[]>(AnafErrors.Http(response.StatusCode));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Apelul e-Factura a eșuat");
            return Result.Failure<byte[]>(AnafErrors.Unavailable);
        }
    }

    private static string? BusinessError(byte[] body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object ? Text(json.RootElement, "eroare") : null;
        }
        catch (JsonException)
        {
            return null;
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

    /// <summary><c>data_creare</c> vine ca <c>yyyyMMddHHmm</c>, ora României.</summary>
    private static DateTime AnafTime(string? value) =>
        DateTime.TryParseExact(value, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local)
            ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Bucharest)
            : DateTime.UtcNow;

    /// <summary>Expirarea din JWT (<c>exp</c>), fără validarea semnăturii: o face ANAF la fiecare apel.</summary>
    internal static DateTime? JwtExpiry(string jwt)
    {
        string[] parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("exp", out JsonElement exp) && exp.TryGetInt64(out long seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }
}

internal static class AnafErrors
{
    public static readonly Error NotConfigured = Error.Problem(
        "Anaf.NotConfigured",
        "Conexiunea ANAF nu e configurată (AnafOAuth:ClientId, AnafOAuth:ClientSecret).");

    public static readonly Error TokenRejected = Error.Problem("Anaf.TokenRejected", "ANAF a refuzat autorizarea. Conectează din nou contul ANAF.");

    public static readonly Error Unavailable = Error.Problem("Anaf.Unavailable", "ANAF nu răspunde. Încearcă din nou mai târziu.");

    public static Error Business(string message) => Error.Problem("Anaf.Error", $"ANAF: {message}");

    public static Error Http(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            Error.Problem("Anaf.Unauthorized", "ANAF a refuzat accesul (tokenul nu mai e valid). Conectează din nou contul ANAF."),
        HttpStatusCode.TooManyRequests => Error.Problem("Anaf.RateLimited", "Prea multe cereri către ANAF. Reîncearcă peste un minut."),
        _ => Error.Problem("Anaf.Http", $"ANAF a răspuns cu HTTP {(int)status}."),
    };
}
