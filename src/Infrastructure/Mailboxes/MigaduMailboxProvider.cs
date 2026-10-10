using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions.Services;
using Application.Mailboxes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Mailboxes;

/// <summary>
/// Adaptorul Migadu (<c>https://api.migadu.com/v1/</c>, HTTP Basic cu emailul contului și cheia API).
///
/// API-ul e în beta: timeout scurt, reîncercare cu pauză crescătoare pe erori de rețea și 5xx,
/// niciodată pe 4xx (un refuz rămâne refuz). Corpul răspunsului de eroare se loghează întreg;
/// corpul cererii, niciodată: conține parole.
/// </summary>
internal sealed partial class MigaduMailboxProvider : IMailboxProvider
{
    public static readonly Uri BaseUri = new("https://api.migadu.com/v1/");
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private const int MaxAttempts = 3;

    /// <summary>Valoarea Migadu pentru „identitatea are parola ei”, nu pe cea a mailbox-ului.</summary>
    private const string OwnCredentialMode = "custom";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly MailboxOptions _options;
    private readonly ILogger<MigaduMailboxProvider> _logger;

    public MigaduMailboxProvider(HttpClient http, IOptions<MailboxOptions> options, ILogger<MigaduMailboxProvider> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress = BaseUri;
        _http.Timeout = Timeout;
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiUser}:{_options.ApiKey}")));
    }

    private string Mailboxes => $"domains/{Uri.EscapeDataString(_options.Domain)}/mailboxes";

    private string Mailbox(string localPart) => $"{Mailboxes}/{Uri.EscapeDataString(localPart)}";

    public async Task<bool> MailboxExistsAsync(string localPart, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, Mailbox(localPart), null, "verificare mailbox", ct, throwOnClientError: false);

        // Documentația nu spune ce întoarce un mailbox inexistent: 404 sau 400-ul generic. Amândouă
        // înseamnă „nu-l am”. Dacă ne înșelăm, crearea e refuzată de Migadu, nu dublată.
        return response.IsSuccessStatusCode;
    }

    public async Task<MailboxResult> CreateMailboxAsync(CreateMailboxRequest req, CancellationToken ct)
    {
        var body = new MailboxBody
        {
            Name = req.Name,
            LocalPart = req.LocalPart,
            Password = req.Password,
            MaySend = true,
            MayReceive = true,
            MayAccessImap = true,
            MayAccessPop3 = false,
            MayAccessManagesieve = false,
        };

        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, Mailboxes, body, "creare mailbox", ct);
        AddressBody? created = await ReadAsync(response, ct);
        return new MailboxResult(req.LocalPart, created?.Address ?? $"{req.LocalPart}@{_options.Domain}");
    }

    public async Task SetMailboxPasswordAsync(string localPart, string password, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Put, Mailbox(localPart), new MailboxBody { Password = password }, "schimbare parolă mailbox", ct);
    }

    public async Task SetRecoveryEmailAsync(string localPart, string email, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Put, Mailbox(localPart), new MailboxBody { PasswordRecoveryEmail = email }, "email de recuperare", ct);
    }

    public async Task<IdentityResult> CreateIdentityAsync(string mailboxLocalPart, CreateIdentityRequest req, CancellationToken ct)
    {
        var body = new MailboxBody
        {
            Name = req.Name,
            LocalPart = req.LocalPart,
            PasswordUse = OwnCredentialMode,
            Password = req.Password,
            MaySend = true,
            MayAccessImap = true,
            MayAccessPop3 = false,
            MayAccessManagesieve = false,
        };

        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"{Mailbox(mailboxLocalPart)}/identities", body, "creare identitate", ct);
        AddressBody? created = await ReadAsync(response, ct);
        return new IdentityResult(req.LocalPart, created?.Address ?? $"{req.LocalPart}@{_options.Domain}");
    }

    public async Task DeleteIdentityAsync(string mailboxLocalPart, string identityLocalPart, CancellationToken ct)
    {
        string path = $"{Mailbox(mailboxLocalPart)}/identities/{Uri.EscapeDataString(identityLocalPart)}";

        // Există? Dacă nu, nu e nimic de șters, iar un refuz al ștergerii ar fi o eroare falsă.
        using HttpResponseMessage existing = await SendAsync(HttpMethod.Get, path, null, "verificare identitate", ct, throwOnClientError: false);
        if (!existing.IsSuccessStatusCode)
        {
            return;
        }

        using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, path, null, "ștergere identitate", ct);
    }

    public async Task<MailboxUsage> GetDomainUsageAsync(CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"domains/{Uri.EscapeDataString(_options.Domain)}/usage", null, "consum domeniu", ct);
        UsageBody usage = await response.Content.ReadFromJsonAsync<UsageBody>(Json, ct)
            ?? throw new MailboxProviderException("Migadu a întors un răspuns gol la consumul domeniului.");
        return new MailboxUsage(usage.Incoming, usage.Outgoing, usage.Storage);
    }

    private static async Task<AddressBody?> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<AddressBody>(Json, ct);
        }
        catch (JsonException)
        {
            // Operația a reușit (2xx); un corp neașteptat nu o anulează.
            return null;
        }
    }

    /// <summary>
    /// Trimite cererea. Reîncearcă doar ce are sens: rețea căzută, timeout, 5xx. Un 4xx se întoarce
    /// la prima încercare (sau aruncă, după <paramref name="throwOnClientError"/>).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        MailboxBody? body,
        string operation,
        CancellationToken ct,
        bool throwOnClientError = true)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiUser) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new MailboxProviderException("Migadu nu este configurat: lipsesc MIGADU_API_USER sau MIGADU_API_KEY.");
        }

        HttpStatusCode? status = null;
        Exception? failure = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 2)), ct);
            }

            HttpResponseMessage? response = null;
            failure = null;
            try
            {
                using var request = new HttpRequestMessage(method, path);
                if (body is not null)
                {
                    request.Content = JsonContent.Create(body, new MediaTypeHeaderValue("application/json"), Json);
                }

                response = await _http.SendAsync(request, ct);
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException && !ct.IsCancellationRequested)
            {
                failure = exception;
            }

            if (response is not null && (int)response.StatusCode < 500)
            {
                if (response.IsSuccessStatusCode || !throwOnClientError)
                {
                    return response;
                }

                string detail = await response.Content.ReadAsStringAsync(ct);
                LogRefused(_logger, operation, (int)response.StatusCode, detail);
                response.Dispose();
                throw new MailboxProviderException($"Migadu a refuzat operația „{operation}” ({(int)response.StatusCode}): {Shorten(detail)}");
            }

            status = response?.StatusCode;
            response?.Dispose();
        }

        LogUnavailable(_logger, operation, MaxAttempts, (int?)status, failure);
        throw failure is null
            ? new MailboxProviderException($"Migadu nu răspunde la „{operation}” ({(int?)status}).")
            : new MailboxProviderException($"Migadu nu răspunde la „{operation}”.", failure);
    }

    private static string Shorten(string detail) => detail.Length > 400 ? detail[..400] : detail;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Migadu a refuzat „{Operation}” cu {Status}: {Body}")]
    private static partial void LogRefused(ILogger logger, string operation, int status, string body);

    [LoggerMessage(Level = LogLevel.Error, Message = "Migadu nu răspunde la „{Operation}” după {Attempts} încercări (status {Status}).")]
    private static partial void LogUnavailable(ILogger logger, string operation, int attempts, int? status, Exception? exception);

    /// <summary>Corpul cererilor de mailbox și identitate: se scriu doar câmpurile puse.</summary>
    private sealed class MailboxBody
    {
        public string? Name { get; init; }
        public string? LocalPart { get; init; }
        public string? PasswordUse { get; init; }
        public string? Password { get; init; }
        public string? PasswordRecoveryEmail { get; init; }
        public bool? MaySend { get; init; }
        public bool? MayReceive { get; init; }
        public bool? MayAccessImap { get; init; }

        [JsonPropertyName("may_access_pop3")]
        public bool? MayAccessPop3 { get; init; }
        public bool? MayAccessManagesieve { get; init; }
    }

    private sealed record AddressBody(string? Address);

    private sealed record UsageBody(int Incoming, int Outgoing, decimal Storage);
}
