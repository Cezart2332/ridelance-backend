using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Application.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Sms;

/// <summary>
/// Trimite SMS-uri prin API-ul REST al Twilio.
/// </summary>
/// <remarks>
/// <para>
/// Un singur POST cu formular și autentificare Basic — nu merită un SDK pentru atât, mai ales
/// unul care aduce cu el o listă întreagă de dependențe pentru restul platformei Twilio.
/// </para>
/// <para>
/// Netestat împotriva API-ului real: contul nu e încă deschis, deci nu există credențiale cu care
/// să fi plecat un mesaj. Forma cererii e cea din documentația Twilio, dar prima trimitere
/// adevărată e prima verificare adevărată.
/// </para>
/// </remarks>
internal sealed class TwilioSmsService(
    HttpClient httpClient,
    IOptions<SmsOptions> options,
    ILogger<TwilioSmsService> logger) : ISmsService
{
    /// <summary>Baza API-ului. Contul intră în cale, deci adresa completă se compune la trimitere.</summary>
    private static readonly Uri ApiBase = new("https://api.twilio.com/2010-04-01/Accounts/");

    private readonly SmsOptions _options = options.Value;

    public async Task<Result> SendAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            logger.LogWarning("SMS necoonfigurat: mesajul către {Phone} nu a plecat.", Mask(phoneNumber));
            return Result.Failure(SmsErrors.NotConfigured);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(ApiBase, string.Create(CultureInfo.InvariantCulture, $"{_options.AccountSid}/Messages.json")));

        request.Content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("To", phoneNumber),
            new KeyValuePair<string, string>("From", _options.From!),
            new KeyValuePair<string, string>("Body", message),
        ]);

        string credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));

        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }

            // Corpul intră în log, nu în răspuns: conține numărul, iar mesajele Twilio sunt
            // scrise pentru cel care a integrat, nu pentru cel care așteaptă un cod.
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "Twilio a refuzat SMS-ul către {Phone}: {Status} {Body}",
                Mask(phoneNumber),
                response.StatusCode,
                body);

            return Result.Failure(SmsErrors.SendFailed);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Twilio nu răspunde. SMS-ul către {Phone} nu a plecat.", Mask(phoneNumber));
            return Result.Failure(SmsErrors.SendFailed);
        }
        catch (TaskCanceledException exception)
        {
            logger.LogError(exception, "Twilio a depășit timpul de așteptare pentru {Phone}.", Mask(phoneNumber));
            return Result.Failure(SmsErrors.SendFailed);
        }
    }

    /// <summary>Ultimele trei cifre sunt destule ca să recunoști numărul într-un log. Restul nu.</summary>
    private static string Mask(string phoneNumber) =>
        phoneNumber.Length <= 3 ? "***" : $"***{phoneNumber[^3..]}";
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
