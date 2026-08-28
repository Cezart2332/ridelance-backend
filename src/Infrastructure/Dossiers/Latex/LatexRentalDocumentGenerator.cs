using Application.Abstractions.Dossiers;

namespace Infrastructure.Dossiers.Latex;

/// <summary>Contractul și procesele-verbale, tipărite cu LaTeX.</summary>
internal sealed class LatexRentalDocumentGenerator(LatexPdfCompiler compiler) : IRentalDocumentGenerator
{
    public Task<byte[]> GenerateAsync(RentalDocumentData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        return compiler.CompileAsync(RentalDocumentLatex.Build(data), cancellationToken);
    }
}
