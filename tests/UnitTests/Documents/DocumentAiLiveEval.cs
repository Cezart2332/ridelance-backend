using System.Text;
using Application.Abstractions.Ai;
using Application.Documents.AiVerification;
using Domain.Documents;
using Infrastructure.Ai;
using Infrastructure.Dossiers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Documents;

/// <summary>
/// Evaluarea pe documente reale: trece un folder de acte adevărate și false prin modelul real și
/// prin regulile din C#, și scrie un tabel cu verdictul fiecăruia. Cu el se calibrează promptul.
///
/// Nu rulează în CI: fără <c>DOC_AI_EVAL_DIR</c> și <c>OPENROUTER_API_KEY</c> iese imediat. Folderul
/// are forma <c>{real,fake}/&lt;Categorie&gt;/fișier</c> (ex. <c>fake/AdeverintaMedicala/word.pdf</c>)
/// și nu se urcă în git — are date personale. Rulare:
/// <code>DOC_AI_EVAL_DIR=../test-data/onboarding-docs OPENROUTER_API_KEY=… dotnet test --filter DocumentAiLiveEval</code>
/// </summary>
public sealed class DocumentAiLiveEval(ITestOutputHelper output)
{
    [Fact]
    public async Task Run_the_model_over_the_local_set()
    {
        string? root = Environment.GetEnvironmentVariable("DOC_AI_EVAL_DIR");
        string? apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(apiKey) || !Directory.Exists(root))
        {
            return;
        }

        var options = new OpenRouterOptions { ApiKey = apiKey };
        string? model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL");
        if (!string.IsNullOrWhiteSpace(model))
        {
            options.Model = model;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var analyzer = new OpenRouterDocumentAiAnalyzer(http, Options.Create(options), NullLogger<OpenRouterDocumentAiAnalyzer>.Instance);
        var forensics = new PdfForensics(NullLogger<PdfForensics>.Instance);
        var table = new StringBuilder();

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string[] parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
            if (parts.Length < 3 || !Enum.TryParse(parts[1], out DocumentCategory category) ||
                DocumentAiCatalog.For(category) is not { } expectation)
            {
                continue;
            }

            string contentType = Path.GetExtension(file).ToUpperInvariant() switch
            {
                ".PDF" => "application/pdf",
                ".PNG" => "image/png",
                _ => "image/jpeg",
            };
            byte[] bytes = await File.ReadAllBytesAsync(file);

            SharedKernel.Result<DocumentAiAnalysisResult> result = await analyzer.AnalyzeAsync(
                new DocumentAiAnalysisRequest(
                    bytes,
                    contentType,
                    Path.GetFileName(file),
                    expectation.Label,
                    expectation.Details,
                    expectation.ExpectsExpiryDate,
                    expectation.FieldSpecs.Select(f => new AiFieldRequest(f.Key, f.Description, f.Type.ToString(), f.Required)).ToList(),
                    expectation.AuthenticityHints),
                CancellationToken.None);

            if (result.IsFailure)
            {
                table.AppendLine(Invariant, $"{parts[0],-5} {category,-22} {parts[^1],-30} EROARE {result.Error.Description}");
                continue;
            }

            DocumentAiAnalysisResult r = result.Value;
            AuthenticityVerdict verdict = DocumentAuthenticityEvaluator.Evaluate(expectation, r.Authenticity, forensics.Inspect(bytes, contentType));
            string? itp = expectation.ExpiryFromField is { } key
                ? DocumentFieldRules.LatestItpDate(
                    r.Fields.FirstOrDefault(f => f.Key == key)?.Value,
                    DocumentDateValidator.Parse(r.Fields.FirstOrDefault(f => f.Key == "data_prima_inmatriculare")?.Value),
                    DocumentDateValidator.Parse(r.Fields.FirstOrDefault(f => f.Key == "data_inmatriculare")?.Value))?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                : null;

            bool rejected = !r.MatchesExpectedType || !r.IsReadable || verdict.IsBlankTemplate;
            string outcome = verdict.Reasons.Count > 0 ? "SUSPECT" : "OK";

            table.AppendLine(
                Invariant,
                $"{parts[0],-5} {category,-22} {parts[^1],-30} {(rejected ? "RESPINS" : outcome),-8} " +
                $"{(itp is null ? string.Empty : $"ITP {itp} ")}{string.Join(" · ", verdict.Reasons)} {r.Reason}");
        }

        output.WriteLine(table.ToString());
        Assert.True(true, "Evaluarea scrie un tabel de citit, nu are verdict de test.");
    }

    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;
}
