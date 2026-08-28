using System.Text;
using Application.Abstractions.Dossiers;

namespace Infrastructure.Dossiers.Latex;

/// <summary>Contractul și procesele-verbale, tipărite cu LaTeX.</summary>
internal sealed class LatexRentalDocumentGenerator(LatexPdfCompiler compiler) : IRentalDocumentGenerator
{
    public async Task<RentalDocumentOutput> GenerateAsync(
        RentalDocumentData data,
        IReadOnlyDictionary<int, RentalSignature> signatures,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        string source = RentalDocumentLatex.Build(data);

        return new RentalDocumentOutput(
            await compiler.CompileAsync(source, Files(signatures), cancellationToken), source);
    }

    public Task<byte[]> SignAsync(
        string source,
        IReadOnlyDictionary<int, RentalSignature> signatures,
        CancellationToken cancellationToken = default) =>
        compiler.CompileAsync(source, Files(signatures), cancellationToken);

    /// <summary>Semnăturile, ca fișiere lângă sursă. Sursa rămâne neatinsă; șablonul le caută singur.</summary>
    private static Dictionary<string, byte[]> Files(IReadOnlyDictionary<int, RentalSignature> signatures)
    {
        ArgumentNullException.ThrowIfNull(signatures);

        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);

        foreach ((int slot, RentalSignature signature) in signatures)
        {
            files[RentalDocumentLatex.SignatureFileName(slot)] = signature.Image;

            // Fără mențiune, fișierul nu se scrie deloc: șablonul lasă atunci rândul gol, în loc să
            // tipărească o linie goală sub nume.
            if (!string.IsNullOrWhiteSpace(signature.Note))
            {
                files[RentalDocumentLatex.SignatureNoteFileName(slot)] =
                    Encoding.UTF8.GetBytes(LatexText.Inline(signature.Note));
            }
        }

        return files;
    }
}
