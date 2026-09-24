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
    /// gol = implicitul modelului. Pentru generarea de text (descrierea firmei): un om așteaptă în
    /// fața butonului, deci rămâne scurt.
    /// </summary>
    public string? ReasoningEffort { get; set; } = "low";

    /// <summary>
    /// Același lucru, pentru citirea documentelor: <c>high</c>, pentru cifre lungi, adrese și date
    /// citite corect. Actele din dosar se citesc în fundal (<c>DocumentAiVerificationJob</c>);
    /// doar scanarea talonului, la adăugarea mașinii, așteaptă răspunsul — câteva secunde în plus.
    /// Costul în plus e de ordinul câtorva dolari pe lună.
    /// </summary>
    public string? DocumentReasoningEffort { get; set; } = "high";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>Parametrul <c>reasoning</c> din cerere, sau <c>null</c> ca să nu se trimită deloc.</summary>
    public object? Reasoning => ReasoningFor(ReasoningEffort);

    /// <summary>Parametrul <c>reasoning</c> pentru citirea documentelor.</summary>
    public object? DocumentReasoning => ReasoningFor(DocumentReasoningEffort);

    private static object? ReasoningFor(string? effort) =>
        string.IsNullOrWhiteSpace(effort) ? null : new { effort };
}
