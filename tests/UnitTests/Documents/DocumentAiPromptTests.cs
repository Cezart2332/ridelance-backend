using Infrastructure.Ai;
using Shouldly;
using Xunit;

namespace UnitTests.Documents;

/// <summary>
/// Promptul de citire a actelor nu are voie să interzică câmpurile pe care le cere catalogul.
/// Interdicția „NU pune NICIODATĂ în fields CNP, serie sau număr” stătea în promptul de sistem,
/// iar Gemini 3.8 Flash o respecta: buletinele veneau fără CNP, serie și număr.
/// </summary>
public sealed class DocumentAiPromptTests
{
    private static readonly string Prompt = OpenRouterDocumentAiAnalyzer.BuildSystemPrompt();

    [Fact]
    public void Promptul_nu_interzice_CNP_seria_si_numarul()
    {
        Prompt.ShouldNotContain("NU pune NICIODATĂ", Case.Insensitive);
        Prompt.ShouldNotContain("NU include în răspuns date personale", Case.Insensitive);
    }

    [Fact]
    public void Promptul_cere_explicit_campurile_sensibile()
    {
        Prompt.ShouldContain("inclusiv CNP-ul, seria și numărul");
    }
}
