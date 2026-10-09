using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Application.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Sms;

/// <summary>Contul Twilio și serviciul Verify. Secțiunea <c>Twilio</c> din configurație.</summary>
public sealed class TwilioOptions
{
    public const string SectionName = "Twilio";

    /// <summary>„AC…” din consola Twilio.</summary>
    public string? AccountSid { get; set; }

    public string? AuthToken { get; set; }

    /// <summary>„VA…”: serviciul Verify creat în consolă (Verify → Services).</summary>
    public string? VerifyServiceSid { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid)
        && !string.IsNullOrWhiteSpace(AuthToken)
        && !string.IsNullOrWhiteSpace(VerifyServiceSid);
}

/// <summary>
/// Codurile de confirmare prin Twilio Verify. Twilio generează codul, îl trimite de pe numerele
/// lui (fără un număr cumpărat de noi) și îl verifică; mesajele sunt tranzacționale, nu marketing.
/// </summary>
/// <remarks>
/// Twilio șterge o verificare după 10 minute, la aprobare sau după prea multe încercări greșite; o
/// verificare de cod pe ea întoarce atunci 404 — pentru om, „codul a expirat, cere altul”.
/// </remarks>
internal sealed class TwilioVerifyService(
    HttpClient httpClient,
    IOptions<TwilioOptions> options,
    ILogger<TwilioVerifyService> logger) : IPhoneCodeVerifier
{
    private static readonly Uri ServicesUri = new("https://verify.twilio.com/v2/Services/");

    /// <summary>Prea multe coduri cerute pentru același număr într-un interval scurt.</summary>
    private const int MaxSendAttemptsCode = 60203;

    private readonly TwilioOptions _options = options.Value;

    public bool IsEnabled => _options.IsConfigured;

    public async Task<Result> SendCodeAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Result.Failure(SmsErrors.NotConfigured);
        }

        try
        {
            using HttpResponseMessage response = await PostAsync(
                "Verifications",
                [
                    new("To", phoneNumber),
                    new("Channel", "sms"),
                    // Mesajul în română, oricare ar fi prefixul numărului.
                    new("Locale", "ro"),
                ],
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }

            TwilioError? error = await ReadErrorAsync(response, cancellationToken);
            logger.LogWarning(
                "Twilio Verify a refuzat trimiterea către {Phone}: {Status} {Code} {Message}",
                Mask(phoneNumber), (int)response.StatusCode, error?.Code, error?.Message);

            return Result.Failure(error?.Code == MaxSendAttemptsCode || response.StatusCode == HttpStatusCode.TooManyRequests
                ? TwilioErrors.TooManySends
                : SmsErrors.SendFailed);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "Twilio Verify nu răspunde. Codul către {Phone} nu a plecat.", Mask(phoneNumber));
            return Result.Failure(SmsErrors.SendFailed);
        }
    }

    public async Task<Result<PhoneCodeCheck>> CheckCodeAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Result.Failure<PhoneCodeCheck>(SmsErrors.NotConfigured);
        }

        try
        {
            using HttpResponseMessage response = await PostAsync(
                "VerificationCheck",
                [new("To", phoneNumber), new("Code", code)],
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return PhoneCodeCheck.Expired;
            }

            if (!response.IsSuccessStatusCode)
            {
                TwilioError? error = await ReadErrorAsync(response, cancellationToken);
                logger.LogWarning(
                    "Twilio Verify a refuzat verificarea pentru {Phone}: {Status} {Code} {Message}",
                    Mask(phoneNumber), (int)response.StatusCode, error?.Code, error?.Message);
                return Result.Failure<PhoneCodeCheck>(TwilioErrors.CheckFailed);
            }

            VerificationResponse? body = await response.Content.ReadFromJsonAsync<VerificationResponse>(cancellationToken);
            return body?.Status switch
            {
                "approved" => PhoneCodeCheck.Approved,
                "pending" => PhoneCodeCheck.Wrong,
                _ => PhoneCodeCheck.Expired,
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogError(exception, "Twilio Verify nu răspunde la verificarea pentru {Phone}.", Mask(phoneNumber));
            return Result.Failure<PhoneCodeCheck>(TwilioErrors.CheckFailed);
        }
    }

    private async Task<HttpResponseMessage> PostAsync(
        string resource,
        IEnumerable<KeyValuePair<string, string>> form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(ServicesUri, $"{_options.VerifyServiceSid}/{resource}"));

        string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(form);

        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task<TwilioError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TwilioError>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>În loguri doar ultimele trei cifre: numărul e dată personală.</summary>
    private static string Mask(string phoneNumber) =>
        phoneNumber.Length <= 3 ? "***" : $"***{phoneNumber[^3..]}";

    private sealed record VerificationResponse([property: JsonPropertyName("status")] string? Status);

    private sealed record TwilioError(
        [property: JsonPropertyName("code")] int? Code,
        [property: JsonPropertyName("message")] string? Message);
}

internal static class SmsErrors
{
    public static readonly Error NotConfigured = Error.Problem(
        "Sms.NotConfigured",
        "Trimiterea prin SMS nu e configurată.");

    public static readonly Error SendFailed = Error.Problem(
        "Sms.SendFailed",
        "Nu am putut trimite SMS-ul. Încearcă din nou în câteva minute.");
}

internal static class TwilioErrors
{
    public static readonly Error TooManySends = Error.Problem(
        "Sms.TooManySends",
        "Ai cerut prea multe coduri pe numărul ăsta. Încearcă din nou peste câteva minute.");

    public static readonly Error CheckFailed = Error.Problem(
        "Sms.CheckFailed",
        "Nu am putut verifica codul acum. Încearcă din nou în câteva momente.");
}
