using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Abstractions.Anaf;
using Domain.Accounting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Accounting.Anaf;

/// <summary>Configurarea clientului pentru <c>ridelance-anaf-validator</c> (secțiunea <c>AnafValidator</c>).</summary>
public sealed class AnafValidatorOptions
{
    public const string SectionName = "AnafValidator";

    /// <summary>Adresa internă a serviciului (ex. <c>http://anaf-validator:8080</c>). Gol = nivelul 3 indisponibil.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Tokenul intern, trimis în <c>X-Internal-Token</c>.</summary>
    public string? Token { get; init; }

    /// <summary>Timeout-ul unei cereri; DUKIntegrator are propriul timeout (60 s implicit).</summary>
    public int TimeoutSeconds { get; init; } = 90;

    /// <summary>Reîncercări după o eroare tranzitorie (rețea, 5xx, 429). Maximum 2 (spec B4).</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Prima pauză dintre încercări; se dublează la fiecare reîncercare.</summary>
    public int RetryDelayMilliseconds { get; init; } = 1000;
}

/// <summary>
/// Clientul HTTP pentru nivelul 3 de validare: <c>POST /v1/validate</c> în modul
/// <c>VALIDATE_AND_PDF</c>. Un XML invalid e un răspuns 200 cu <c>valid = false</c>; un eșec al
/// clientului înseamnă că serviciul nu a putut fi folosit.
/// </summary>
internal sealed class AnafValidatorClient(HttpClient http, IOptions<AnafValidatorOptions> options, ILogger<AnafValidatorClient> logger)
    : IAnafValidatorClient
{
    private const string ValidatePath = "v1/validate";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly HttpStatusCode[] Transient =
    [
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
        HttpStatusCode.TooManyRequests,
    ];

    public async Task<Result<AnafValidatorResult>> ValidateAsync(
        DeclarationType type,
        string validatorVersion,
        byte[] xml,
        string correlationId,
        CancellationToken cancellationToken)
    {
        AnafValidatorOptions settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return Result.Failure<AnafValidatorResult>(Unavailable("serviciul nu e configurat (AnafValidator:BaseUrl)."));
        }

        Uri endpoint = new UriBuilder(settings.BaseUrl) { Path = ValidatePath }.Uri;
        int retries = Math.Clamp(settings.MaxRetries, 0, 2);
        string? lastProblem = null;

        for (int attempt = 0; attempt <= retries; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(settings.RetryDelayMilliseconds * Math.Pow(2, attempt - 1)), cancellationToken);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));
            try
            {
                using HttpRequestMessage request = Request(endpoint, settings.Token, type, validatorVersion, xml, correlationId);
                using HttpResponseMessage response = await http.SendAsync(request, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    ValidateResponse? body = await response.Content.ReadFromJsonAsync<ValidateResponse>(Json, timeout.Token);
                    return body is null
                        ? Result.Failure<AnafValidatorResult>(Unavailable("răspuns gol."))
                        : body.ToResult();
                }

                string detail = await ErrorDetail(response, timeout.Token);
                if (!Transient.Contains(response.StatusCode))
                {
                    return Result.Failure<AnafValidatorResult>(Unavailable(Explain(response.StatusCode, validatorVersion, detail)));
                }

                lastProblem = Explain(response.StatusCode, validatorVersion, detail);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                lastProblem = exception is TaskCanceledException
                    ? $"nu a răspuns în {settings.TimeoutSeconds} s."
                    : "serviciul nu răspunde.";
            }

            logger.LogWarning(
                "Validatorul ANAF, încercarea {Attempt} pentru {DeclarationType} ({CorrelationId}): {Problem}",
                attempt + 1, type, correlationId, lastProblem);
        }

        return Result.Failure<AnafValidatorResult>(Unavailable(lastProblem ?? "serviciul nu răspunde."));
    }

    // Conținuturile trec în proprietatea formularului, iar formularul a cererii: le eliberează cererea.
#pragma warning disable CA2000
    private static HttpRequestMessage Request(Uri endpoint, string? token, DeclarationType type, string validatorVersion, byte[] xml, string correlationId)
    {
        var file = new ByteArrayContent(xml);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        var form = new MultipartFormDataContent
        {
            { file, "xml", $"{type}.xml" },
            { new StringContent(type.ToString()), "declarationType" },
            { new StringContent(validatorVersion), "validatorVersion" },
            { new StringContent("VALIDATE_AND_PDF"), "mode" },
            { new StringContent(correlationId), "correlationId" },
        };

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = form };
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Add("X-Internal-Token", token);
        }

        request.Headers.Add("X-Correlation-Id", correlationId);
        return request;
    }
#pragma warning restore CA2000

    private static async Task<string?> ErrorDetail(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            ErrorResponse? error = await response.Content.ReadFromJsonAsync<ErrorResponse>(Json, cancellationToken);
            return error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Explain(HttpStatusCode status, string validatorVersion, string? detail) => status switch
    {
        HttpStatusCode.Unauthorized => "tokenul intern e greșit (AnafValidator:Token).",
        HttpStatusCode.NotFound => $"versiunea de validator {validatorVersion} nu e instalată în serviciu.",
        HttpStatusCode.RequestEntityTooLarge => "XML-ul depășește limita serviciului.",
        HttpStatusCode.UnprocessableEntity => $"XML-ul nu e bine format{(detail is null ? "." : $": {detail}")}",
        HttpStatusCode.GatewayTimeout => "DUKIntegrator nu a terminat la timp.",
        _ => $"HTTP {(int)status}{(detail is null ? "." : $": {detail}")}",
    };

    private static Error Unavailable(string reason) => Error.Problem("Accounting.AnafValidatorUnavailable", $"Validatorul ANAF: {reason}");

    private sealed record ErrorResponse(int Status, string? Error, string? Message, string? CorrelationId);

    private sealed record ValidateResponse(
        bool Valid,
        string? DeclarationType,
        string? ValidatorVersion,
        List<AnafValidatorMessage>? Errors,
        List<AnafValidatorMessage>? Warnings,
        string? RawOutput,
        string? PdfBase64,
        long DurationMs,
        string? CorrelationId)
    {
        public AnafValidatorResult ToResult() => new(
            Valid,
            Errors ?? [],
            Warnings ?? [],
            RawOutput ?? string.Empty,
            string.IsNullOrEmpty(PdfBase64) ? null : Convert.FromBase64String(PdfBase64),
            DurationMs,
            CorrelationId);
    }
}
