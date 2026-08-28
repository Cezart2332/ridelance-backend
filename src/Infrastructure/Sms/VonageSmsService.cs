using System.Text.Json;
using Application.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Sms;

/// <summary>
/// Trimite SMS-uri prin API-ul SMS al Vonage.
/// </summary>
/// <remarks>
/// <para>
/// Un singur POST cu formular — nu merită un SDK pentru atât.
/// </para>
/// <para>
/// Vonage răspunde <c>200 OK</c> și când mesajul a fost respins: rezultatul stă în
/// <c>messages[0].status</c>, unde <c>"0"</c> înseamnă acceptat, iar orice altceva e un cod de
/// eroare însoțit de <c>error-text</c>. Deci codul de stare HTTP nu spune nimic singur, și
/// verificarea se face pe corp.
/// </para>
/// <para>
/// „Acceptat" înseamnă preluat de Vonage, nu ajuns pe telefon. Livrarea propriu-zisă vine abia
/// printr-un webhook de status, pe care nu-l ascultăm: pentru un cod de confirmare, dovada că a
/// ajuns e că omul îl tastează.
/// </para>
/// </remarks>
internal sealed class VonageSmsService(
    HttpClient httpClient,
    IOptions<SmsOptions> options,
    ILogger<VonageSmsService> logger) : ISmsService
{
    private static readonly Uri Endpoint = new("https://rest.nexmo.com/sms/json");

    /// <summary>Codul de stare pe care Vonage îl întoarce când a preluat mesajul.</summary>
    private const string AcceptedStatus = "0";

    private readonly SmsOptions _options = options.Value;

    public async Task<Result> SendAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            logger.LogWarning("SMS neconfigurat: mesajul către {Phone} nu a plecat.", Mask(phoneNumber));
            return Result.Failure(SmsErrors.NotConfigured);
        }

        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("api_key", _options.ApiKey!),
            new KeyValuePair<string, string>("api_secret", _options.ApiSecret!),
            // Vonage vrea numărul fără plus: 407… , nu +407… .
            new KeyValuePair<string, string>("to", phoneNumber.TrimStart('+')),
            new KeyValuePair<string, string>("from", _options.From!),
            new KeyValuePair<string, string>("text", message),
        ]);

        try
        {
            using HttpResponseMessage response = await httpClient.PostAsync(Endpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Vonage a răspuns {Status} la SMS-ul către {Phone}.",
                    (int)response.StatusCode,
                    Mask(phoneNumber));

                return Result.Failure(SmsErrors.SendFailed);
            }

            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return Read(document.RootElement, phoneNumber);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Vonage nu răspunde. SMS-ul către {Phone} nu a plecat.", Mask(phoneNumber));
            return Result.Failure(SmsErrors.SendFailed);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Vonage a depășit timpul de așteptare pentru {Phone}.", Mask(phoneNumber));
            return Result.Failure(SmsErrors.SendFailed);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Răspuns neinteligibil de la Vonage pentru {Phone}.", Mask(phoneNumber));
            return Result.Failure(SmsErrors.SendFailed);
        }
    }

    /// <summary>Rezultatul real al trimiterii, din corpul răspunsului.</summary>
    private Result Read(JsonElement root, string phoneNumber)
    {
        if (!root.TryGetProperty("messages", out JsonElement messages) || messages.GetArrayLength() == 0)
        {
            logger.LogError("Vonage n-a întors niciun mesaj pentru {Phone}.", Mask(phoneNumber));
            return Result.Failure(SmsErrors.SendFailed);
        }

        // Un singur destinatar, deci un singur mesaj — cu excepția textelor lungi, pe care Vonage
        // le taie în bucăți. Atunci prima bucată respinsă e destulă ca să considerăm eșec: un cod
        // de confirmare ajuns pe jumătate nu confirmă nimic. Codul nostru încape oricum într-unul.
        JsonElement first = messages[0];

        string? status = first.TryGetProperty("status", out JsonElement statusElement)
            ? statusElement.GetString()
            : null;

        if (string.Equals(status, AcceptedStatus, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        string? errorText = first.TryGetProperty("error-text", out JsonElement errorElement)
            ? errorElement.GetString()
            : null;

        // Textul erorii intră în log, nu în răspuns: e scris pentru cel care a integrat, nu
        // pentru cel care așteaptă un cod.
        logger.LogError(
            "Vonage a respins SMS-ul către {Phone}: status {Status}, {Error}",
            Mask(phoneNumber),
            status,
            errorText);

        return Result.Failure(SmsErrors.SendFailed);
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
