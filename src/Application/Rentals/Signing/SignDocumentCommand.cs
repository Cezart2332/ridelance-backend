using System.Security.Cryptography;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.Rentals;
using SharedKernel;

namespace Application.Rentals.Signing;

/// <param name="SignatureImage">PNG-ul semnăturii, ca data-URL. Vine din aceeași pânză ca la PFA.</param>
/// <param name="Context">IP și user-agent, citite din cerere pe server — niciodată din corpul ei.</param>
public sealed record SignDocumentCommand(
    string Token,
    string SignatureImage,
    SigningContext Context) : ICommand;

public sealed record SigningContext(string? IpAddress, string? UserAgent);

internal sealed class SignDocumentCommandHandler(
    IApplicationDbContext context,
    IFileEncryptionService encryption)
    : ICommandHandler<SignDocumentCommand>
{
    /// <summary>Limita imaginii de semnătură. Aceeași ca la deschiderea PFA.</summary>
    private const int MaxSignatureBytes = 2 * 1024 * 1024;

    public async Task<Result> Handle(SignDocumentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<SignatureRequest> found = await SignatureRequestLookup.FindAsync(
            context, command.Token, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        SignatureRequest request = found.Value;

        byte[] signature;
        try
        {
            string payload = command.SignatureImage;
            int comma = payload.IndexOf(',', StringComparison.Ordinal);
            signature = Convert.FromBase64String(comma >= 0 ? payload[(comma + 1)..] : payload);
        }
        catch (FormatException)
        {
            return Result.Failure(Error.Problem("Signature.Invalid", "Semnătura nu a putut fi citită."));
        }

        if (signature.Length == 0 || signature.Length > MaxSignatureBytes)
        {
            return Result.Failure(Error.Problem("Signature.Invalid", "Semnătura nu a putut fi citită."));
        }

        string fileName = $"semnatura-{request.GeneratedDocument.Rental.PublicCode}.png";
        using var stream = new MemoryStream(signature);
        EncryptedFileResult encrypted = await encryption.EncryptAndSaveAsync(stream, fileName, cancellationToken);

        var image = new Document
        {
            Id = Guid.NewGuid(),
            UserId = request.GeneratedDocument.Rental.OwnerUserId,
            OriginalFileName = fileName,
            StoredFileName = fileName,
            ContentType = "image/png",
            Category = DocumentCategory.SpecimenSemnatura,
            Status = DocumentStatus.Verified,
            Origin = DocumentOrigin.UserUpload,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = signature.Length,
            UploadedAtUtc = DateTime.UtcNow,
            AiStatus = DocumentAiStatus.None,
        };

        context.Documents.Add(image);

        // Amprenta leagă semnătura de fișierul semnat. Dacă documentul se regenerează, hash-ul nu
        // mai corespunde — semnătura nu se poate muta tăcut pe alt conținut.
        byte[] signed = await ReadDocumentAsync(request.GeneratedDocument.DocumentId, cancellationToken);
        request.PayloadHash = Convert.ToHexString(SHA256.HashData([.. signature, .. signed]));

        request.UsedAtUtc = DateTime.UtcNow;
        request.SignatureImageDocumentId = image.Id;
        request.IpAddress = command.Context.IpAddress;
        request.UserAgent = command.Context.UserAgent;

        request.GeneratedDocument.Status = GeneratedDocumentStatus.Signed;
        request.GeneratedDocument.SignedAtUtc = DateTime.UtcNow;
        request.GeneratedDocument.ExternalSignatureRef = request.Id.ToString();

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Fișierul semnat, ca octeți, pentru amprentă. Nu se modifică — doar se citește.</summary>
    private async Task<byte[]> ReadDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        Document? document = await context.Documents.FindAsync([documentId], cancellationToken);
        if (document is null)
        {
            return [];
        }

        using Stream stream = await encryption.DecryptAndReadAsync(
            document.EncryptedFilePath, document.EncryptionIv, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
