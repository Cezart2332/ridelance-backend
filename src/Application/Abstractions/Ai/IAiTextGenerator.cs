using SharedKernel;

namespace Application.Abstractions.Ai;

/// <summary>O cerere de text către model, cu răspuns JSON.</summary>
/// <param name="Temperature">
/// 0 pentru extragere, mai sus pentru scris. Aici chiar vrem variație: trei propuneri identice
/// nu sunt trei propuneri.
/// </param>
public sealed record AiTextRequest(string SystemPrompt, string UserPrompt, double Temperature);

/// <summary>
/// Generare de text cu model de limbaj, separată de <see cref="IDocumentAiAnalyzer"/>.
/// </summary>
/// <remarks>
/// Două abstracții, nu una, fiindcă sunt două meserii diferite: analizorul primește un fișier și
/// are voie doar să citească din el, generatorul primește fapte și scrie text nou. Un singur
/// contract le-ar fi obligat pe amândouă la parametri pe care doar una îi folosește.
/// </remarks>
public interface IAiTextGenerator
{
    /// <summary>Cere modelului un obiect JSON și îl deserializează în <typeparamref name="T"/>.</summary>
    Task<Result<T>> GenerateAsync<T>(AiTextRequest request, CancellationToken cancellationToken)
        where T : class;
}
