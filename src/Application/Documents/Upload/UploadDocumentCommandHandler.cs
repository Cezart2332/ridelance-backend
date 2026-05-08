using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Documents;
using SharedKernel;

namespace Application.Documents.Upload;

internal sealed class UploadDocumentCommandHandler(
    IApplicationDbContext context,
    IFileEncryptionService fileEncryptionService)
    : ICommandHandler<UploadDocumentCommand, Guid>
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "APPLICATION/PDF",
        "IMAGE/JPEG",
        "IMAGE/PNG"
    ];

    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    public async Task<Result<Guid>> Handle(
        UploadDocumentCommand command,
        CancellationToken cancellationToken)
    {
        if (!AllowedContentTypes.Contains(command.ContentType.ToUpperInvariant()))
        {
            return Result.Failure<Guid>(DocumentErrors.InvalidFileType);
        }

        if (command.FileSize > MaxFileSize)
        {
            return Result.Failure<Guid>(DocumentErrors.FileTooLarge);
        }

        string storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(command.FileName)}";

        EncryptedFileResult encryptionResult = await fileEncryptionService.EncryptAndSaveAsync(
            command.FileStream,
            storedFileName,
            cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            PfaRegistrationId = command.PfaRegistrationId,
            OriginalFileName = command.FileName,
            StoredFileName = storedFileName,
            ContentType = command.ContentType,
            Category = command.Category,
            Status = DocumentStatus.Pending,
            EncryptedFilePath = encryptionResult.FilePath,
            EncryptionIv = encryptionResult.Iv,
            FileSize = command.FileSize,
            UploadedAtUtc = DateTime.UtcNow
        };

        context.Documents.Add(document);
        await context.SaveChangesAsync(cancellationToken);

        return document.Id;
    }
}
