namespace Infrastructure.Ai;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string? ApiKey { get; set; }

    /// <summary>
    /// Gemini 3.8 Flash: cel mai bun la extragerea câmpurilor din documente (Roboflow Vision Evals,
    /// sept. 2026) și citește nativ PDF-uri și poze. 2.5 Flash dispare din OpenRouter pe 20.10.2026.
    /// </summary>
    public string Model { get; set; } = "google/gemini-3.8-flash";

    /// <summary>
    /// Cât „gândește” modelul înainte să răspundă (<c>minimal</c>, <c>low</c>, <c>medium</c>, <c>high</c>);
    /// gol = implicitul modelului. Pentru citit câmpuri, mai mult nu ajută (în teste scorul chiar
    /// scade), iar gânditul se plătește ca output.
    /// </summary>
    public string? ReasoningEffort { get; set; } = "low";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>Parametrul <c>reasoning</c> din cerere, sau <c>null</c> ca să nu se trimită deloc.</summary>
    public object? Reasoning => string.IsNullOrWhiteSpace(ReasoningEffort) ? null : new { effort = ReasoningEffort };
}
