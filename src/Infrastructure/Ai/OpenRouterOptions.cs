namespace Infrastructure.Ai;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string? ApiKey { get; set; }
    public string Model { get; set; } = "google/gemini-2.5-flash";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
}
