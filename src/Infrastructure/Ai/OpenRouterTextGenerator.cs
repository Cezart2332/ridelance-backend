using System.Text.Json;
using Application.Abstractions.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Ai;

/// <summary>
/// Generare de text prin OpenRouter, cu răspuns cerut ca obiect JSON.
/// </summary>
/// <remarks>
/// Aceeași cheie și același model ca la analiza documentelor — nu adăugăm un al doilea furnizor
/// pentru o a doua întrebuințare. Diferența e temperatura: aici scriem, nu citim.
/// </remarks>
internal sealed class OpenRouterTextGenerator(
    HttpClient httpClient,
    IOptions<OpenRouterOptions> options,
    ILogger<OpenRouterTextGenerator> logger) : IAiTextGenerator
{
    /// <summary>
    /// Modelul răspunde în snake_case pentru că așa i se cere în prompt; aici îl citim la fel.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<Result<T>> GenerateAsync<T>(AiTextRequest request, CancellationToken cancellationToken)
        where T : class
    {
        OpenRouterOptions config = options.Value;

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return Result.Failure<T>(Error.Failure(
                "Ai.NotConfigured",
                "Cheia API OpenRouter nu este configurată."));
        }

        var payload = new
        {
            model = config.Model,
            temperature = request.Temperature,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt },
            },
        };

        Result<string> body = await OpenRouterJson.SendAsync(
            httpClient, config, payload, logger, "generarea de text", cancellationToken);

        if (body.IsFailure)
        {
            return Result.Failure<T>(body.Error);
        }

        Result<string> content = OpenRouterJson.ExtractContent(body.Value);
        if (content.IsFailure)
        {
            logger.LogWarning("Răspunsul OpenRouter nu a putut fi interpretat: {Code}.", content.Error.Code);
            return Result.Failure<T>(content.Error);
        }

        try
        {
            T? parsed = JsonSerializer.Deserialize<T>(content.Value, ReadOptions);
            return parsed is null
                ? Result.Failure<T>(Error.Failure("Ai.EmptyResponse", "Serviciul AI a returnat un răspuns gol."))
                : Result.Success(parsed);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "JSON-ul generat de OpenRouter nu s-a putut deserializa în {Type}.", typeof(T).Name);
            return Result.Failure<T>(Error.Failure(
                "Ai.InvalidResponse",
                "Răspunsul serviciului AI nu a putut fi interpretat."));
        }
    }
}
