using System.Security.Cryptography;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Documents;

/// <summary><c>POST /accounting/pfas/{pfaId}/platform-documents</c> — multipart(file, period).</summary>
public sealed record UploadPlatformDocumentCommand(
    Guid PfaId,
    string Period,
    string FileName,
    string ContentType,
    Stream Content,
    long Size) : ICommand<PlatformDocumentDto>;

/// <summary>
/// Încărcarea unei facturi sau a unui raport Uber/Bolt (spec B1): hash, refuz la duplicat (409 cu
/// documentul existent), fișierul prin mecanismul existent de documente criptate, apoi documentul
/// intră în coada de extracție cu statusul <c>EXTRACTING</c>.
/// </summary>
internal sealed class UploadPlatformDocumentCommandHandler(
    IApplicationDbContext db,
    IFileEncryptionService encryption,
    IUserContext userContext,
    IOptions<AccountingOptions> options)
    : ICommandHandler<UploadPlatformDocumentCommand, PlatformDocumentDto>
{
    public async Task<Result<PlatformDocumentDto>> Handle(UploadPlatformDocumentCommand command, CancellationToken cancellationToken)
    {
        if (!PlatformDocumentSupport.IsValidPeriod(command.Period))
        {
            return Result.Failure<PlatformDocumentDto>(AccountingErrors.InvalidPeriod);
        }

        bool pdf = command.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
                   command.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!pdf)
        {
            return Result.Failure<PlatformDocumentDto>(AccountingErrors.OnlyPdf);
        }

        if (command.Size > options.Value.MaxUploadBytes)
        {
            return Result.Failure<PlatformDocumentDto>(AccountingErrors.FileTooLarge);
        }

        PfaRegistration? pfa = await db.PfaRegistrations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.PfaId, cancellationToken);
        if (pfa is null)
        {
            return Result.Failure<PlatformDocumentDto>(AccountingErrors.PfaNotFound);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, pfa.Id, command.Period, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<PlatformDocumentDto>(writable.Error);
        }

        using var buffer = new MemoryStream();
        await command.Content.CopyToAsync(buffer, cancellationToken);
        string hash = Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));

        var existing = await db.PlatformDocuments
            .Where(d => d.PfaRegistrationId == pfa.Id && d.FileHash == hash)
            .Select(d => new { d.Id, d.SourceDocument.OriginalFileName })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<PlatformDocumentDto>(new DuplicatePlatformDocumentError(existing.Id, existing.OriginalFileName));
        }

        buffer.Position = 0;
        string storedFileName = $"{Guid.NewGuid()}.pdf";
        EncryptedFileResult encrypted = await encryption.EncryptAndSaveAsync(buffer, storedFileName, cancellationToken);

        var file = new Document
        {
            Id = Guid.NewGuid(),
            UserId = pfa.UserId,
            PfaRegistrationId = pfa.Id,
            OriginalFileName = command.FileName,
            StoredFileName = storedFileName,
            ContentType = "application/pdf",
            // Tipul exact (factură / raport, Bolt / Uber) îl dă extracția.
            Category = DocumentCategory.Other,
            Status = DocumentStatus.Verified,
            Origin = DocumentOrigin.AccountingUpload,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = command.Size,
            UploadedAtUtc = DateTime.UtcNow,
            AiStatus = DocumentAiStatus.None,
        };

        var document = new PlatformDocument
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa.Id,
            Period = command.Period,
            SourceDocumentId = file.Id,
            FileHash = hash,
            Status = PlatformDocumentStatus.Extracting,
            UploadedByUserId = userContext.UserId,
            UploadedAtUtc = DateTime.UtcNow,
        };

        db.Documents.Add(file);
        db.PlatformDocuments.Add(document);
        AccountingAudit.Record(db, pfa.Id, nameof(PlatformDocument), document.Id, "UPLOAD", null, new { command.FileName, command.Period }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);

        Dictionary<Guid, UserRef> users = await PlatformDocumentSupport.UsersAsync(db, [userContext.UserId], cancellationToken);
        return new PlatformDocumentDto
        {
            Id = document.Id,
            PfaId = pfa.Id,
            Period = document.Period,
            Platform = null,
            DocumentType = document.DocumentType,
            FileName = file.OriginalFileName,
            Status = document.Status,
            UploadedBy = PlatformDocumentSupport.UserOrSystem(users, userContext.UserId),
            UploadedAt = document.UploadedAtUtc,
        };
    }
}
