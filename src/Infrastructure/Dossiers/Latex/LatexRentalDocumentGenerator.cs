using System.Text;
using Application.Abstractions.Dossiers;

namespace Infrastructure.Dossiers.Latex;

/// <summary>Contractul și procesele-verbale, tipărite cu LaTeX.</summary>
internal sealed class LatexRentalDocumentGenerator(LatexPdfCompiler compiler) : IRentalDocumentGenerator
{
    private static readonly Dictionary<string, byte[]> NoFiles = new(StringComparer.Ordinal);

    public async Task<RentalDocumentOutput> GenerateAsync(
        RentalDocumentData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        string source = RentalDocumentLatex.Build(data);

        return new RentalDocumentOutput(
            await compiler.CompileAsync(source, NoFiles, cancellationToken), source);
    }

    public Task<byte[]> SignAsync(
        string source,
        IReadOnlyDictionary<int, RentalSignature> signatures,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signatures);

        // Sursa rămâne neatinsă; semnăturile ajung fișiere lângă ea, iar șablonul le găsește singur.
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);

        foreach ((int slot, RentalSignature signature) in signatures)
        {
            files[RentalDocumentLatex.SignatureFileName(slot)] = signature.Image;
            files[RentalDocumentLatex.SignatureNoteFileName(slot)] =
                Encoding.UTF8.GetBytes(LatexText.Inline(signature.Note));
        }

        return compiler.CompileAsync(source, files, cancellationToken);
    }
}
