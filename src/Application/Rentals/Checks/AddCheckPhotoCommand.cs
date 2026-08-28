using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Checks;

/// <param name="Slot">Unghiul fotografiat. Unul singur per slot: a doua poză o înlocuiește pe prima.</param>
public sealed record AddCheckPhotoCommand(
    Guid RentalId,
    CheckKind Kind,
    CheckPhotoSlot Slot,
    string FileName,
    string ContentType,
    Stream FileStream,
    long FileSize) : ICommand<Guid>;

internal sealed class AddCheckPhotoCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IFileEncryptionService encryption)
    : ICommandHandler<AddCheckPhotoCommand, Guid>
{
    /// <summary>Limita per fotografie. Un telefon modern trece rar de atât la o poză de mașină.</summary>
    private const long MaxPhotoBytes = 12 * 1024 * 1024;

    public async Task<Result<Guid>> Handle(AddCheckPhotoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<Guid>(Error.Problem(
                "CheckPhoto.NotAnImage",
                "Sloturile de fotografie acceptă doar imagini."));
        }

        if (command.FileSize <= 0 || command.FileSize > MaxPhotoBytes)
        {
            return Result.Failure<Guid>(Error.Problem(
                "CheckPhoto.TooLarge",
                "Fotografia depășește 12 MB."));
        }

        Rental? rental = await context.Rentals
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.Id == command.RentalId && r.OwnerUserId == userContext.UserId,
                cancellationToken);

        if (rental is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Rental.NotFound", "Închirierea nu a fost găsită."));
        }

        CheckRecord? record = await context.CheckRecords
            .Include(c => c.Photos)
            .FirstOrDefaultAsync(c => c.RentalId == rental.Id && c.Kind == command.Kind, cancellationToken);

        if (record is null)
        {
            // Fotografia aparține unei consemnări. Fără ea n-ar avea nici moment, nici kilometraj —
            // adică n-ar proba nimic.
            return Result.Failure<Guid>(Error.Problem(
                "CheckPhoto.NoRecord",
                "Consemnează întâi predarea sau primirea, apoi adaugă fotografiile."));
        }

        EncryptedFileResult encrypted = await encryption.EncryptAndSaveAsync(
            command.FileStream, command.FileName, cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userContext.UserId,
            CarId = rental.CarId,
            OriginalFileName = command.FileName,
            StoredFileName = command.FileName,
            ContentType = command.ContentType,
            Category = DocumentCategory.Other,
            Status = DocumentStatus.Verified,
            Origin = DocumentOrigin.UserUpload,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = command.FileSize,
            UploadedAtUtc = DateTime.UtcNow,
            AiStatus = DocumentAiStatus.None,
        };

        context.Documents.Add(document);

        // Un slot ține o singură fotografie: comparația celor două coloane se face poziție cu
        // poziție, iar două poze pe „Față" ar face-o ambiguă.
        CheckPhoto? existing = record.Photos.Find(p => p.Slot == command.Slot);
        if (existing is not null)
        {
            context.CheckPhotos.Remove(existing);
        }

        var photo = new CheckPhoto
        {
            Id = Guid.NewGuid(),
            CheckRecordId = record.Id,
            Slot = command.Slot,
            DocumentId = document.Id,
        };

        context.CheckPhotos.Add(photo);
        await context.SaveChangesAsync(cancellationToken);

        return photo.Id;
    }
}
