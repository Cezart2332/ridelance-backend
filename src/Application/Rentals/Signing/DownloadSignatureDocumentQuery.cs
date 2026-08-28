using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.Rentals;
using SharedKernel;

namespace Application.Rentals.Signing;

/// <summary>Fișierul de semnat, deschis cu tokenul din email. Fără cont, ca tot fluxul.</summary>
public sealed record DownloadSignatureDocumentQuery(string Token) : IQuery<SignatureFileDto>;

public sealed record SignatureFileDto(byte[] Content, string ContentType, string FileName);

internal sealed class DownloadSignatureDocumentQueryHandler(
    IApplicationDbContext context,
    IFileEncryptionService encryption)
    : IQueryHandler<DownloadSignatureDocumentQuery, SignatureFileDto>
{
    public async Task<Result<SignatureFileDto>> Handle(
        DownloadSignatureDocumentQuery query,
        CancellationToken cancellationToken)
    {
        Result<SignatureRequest> found = await SignatureRequestLookup.FindAsync(
            context, query.Token, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<SignatureFileDto>(found.Error);
        }

        Document? document = await context.Documents
            .FindAsync([found.Value.GeneratedDocument.DocumentId], cancellationToken);

        if (document is null)
        {
            return Result.Failure<SignatureFileDto>(
                Error.NotFound("Document.NotFound", "Fișierul nu a fost găsit."));
        }

        using Stream stream = await encryption.DecryptAndReadAsync(
            document.EncryptedFilePath, document.EncryptionIv, cancellationToken);

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return Result.Success(new SignatureFileDto(
            buffer.ToArray(), document.ContentType, document.OriginalFileName));
    }
}
