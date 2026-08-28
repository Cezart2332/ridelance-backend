using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Domain.Documents;

namespace Application.Rentals.Documents;

/// <summary>Citirea unui specimen de semnătură din stocarea criptată.</summary>
/// <remarks>
/// Aceeași lectură e nevoie și la generare, și la semnare — a doua oară ca varianta semnată să
/// poarte tot semnătura firmei, nu doar pe cea a chiriașului.
/// </remarks>
internal static class StoredSignature
{
    /// <summary>Imaginea semnăturii, sau nimic dacă nu e salvată ori nu se mai găsește.</summary>
    /// <remarks>
    /// Lipsa ei nu e o eroare: linia rămâne goală, de semnat de mână, exact ca înainte să existe
    /// specimene. Un document care nu se mai poate genera pentru că o semnătură s-a pierdut ar fi
    /// mai rău decât unul care se semnează cu pixul.
    /// </remarks>
    public static async Task<byte[]?> ReadAsync(
        IApplicationDbContext context,
        IFileEncryptionService encryption,
        Guid? documentId,
        CancellationToken cancellationToken)
    {
        if (documentId is null)
        {
            return null;
        }

        Document? document = await context.Documents.FindAsync([documentId.Value], cancellationToken);

        if (document is null)
        {
            return null;
        }

        using Stream stream = await encryption.DecryptAndReadAsync(
            document.EncryptedFilePath, document.EncryptionIv, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }
}
