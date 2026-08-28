using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Cars;
using Domain.Companies;
using Domain.Documents;
using Domain.Rentals;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Application.Rentals.Checks;

namespace Application.Rentals.Documents;

public sealed record GenerateRentalDocumentCommand(Guid RentalId, RentalDocumentType Type)
    : ICommand<GeneratedDocumentDto>;

public sealed record GeneratedDocumentDto(
    Guid Id,
    string Type,
    string Status,
    int Version,
    Guid DocumentId,
    Guid? SignedDocumentId,
    DateTime GeneratedAtUtc,
    DateTime? SentAtUtc,
    string? SentToEmail,
    DateTime? SignedAtUtc);

/// <summary>
/// Ce lipsește ca să se poată genera. Se întoarce ca eșec, cu lista, nu ca listă goală de succes:
/// interfața trebuie să deschidă un formular, nu să afișeze un document inexistent.
/// </summary>
public sealed record RentalDocumentBlockedError(IReadOnlyList<MissingField> Missing);

internal sealed class GenerateRentalDocumentCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IRentalDocumentGenerator generator,
    IFileEncryptionService encryption)
    : ICommandHandler<GenerateRentalDocumentCommand, GeneratedDocumentDto>
{
    public async Task<Result<GeneratedDocumentDto>> Handle(
        GenerateRentalDocumentCommand command,
        CancellationToken cancellationToken)
    {
        Rental? rental = await context.Rentals
            .Include(r => r.Tenant)
            .FirstOrDefaultAsync(
                r => r.Id == command.RentalId && r.OwnerUserId == userContext.UserId,
                cancellationToken);

        if (rental is null)
        {
            return Result.Failure<GeneratedDocumentDto>(
                Error.NotFound("Rental.NotFound", "Închirierea nu a fost găsită."));
        }

        Car? car = await context.Cars
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == rental.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<GeneratedDocumentDto>(
                Error.NotFound("Car.NotFound", "Mașina închirierii nu mai există."));
        }

        CompanyProfile? company = await context.CompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userContext.UserId, cancellationToken);

        IReadOnlyList<MissingField> missing =
            RentalDocumentRequirements.For(command.Type, rental, car, company, rental.Tenant);

        if (missing.Count > 0)
        {
            // Câmpurile lipsă merg în `Description`, ca interfața să le poată despacheta și cere
            // exact pe ele. Codul e stabil; textul e pentru cine citește logurile.
            return Result.Failure<GeneratedDocumentDto>(Error.Problem(
                "RentalDocument.MissingFields",
                string.Join("|", missing.Select(m => $"{m.Field};{m.Label};{m.Owner}"))));
        }

        FleetRentalDefaults? defaults = await context.FleetRentalDefaults
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.OwnerUserId == userContext.UserId, cancellationToken);

        RentalDocumentData data = RentalDocumentComposer.Compose(
            command.Type, rental, car, company!, rental.Tenant, defaults?.DefaultConditions);

        RentalDocumentOutput printed = await generator.GenerateAsync(data, cancellationToken);

        int version = await context.GeneratedDocuments
            .CountAsync(d => d.RentalId == rental.Id && d.Type == command.Type.ToString(), cancellationToken) + 1;

        string fileName = $"{data.PublicCode}-{command.Type}-v{version}.pdf";

        using var stream = new MemoryStream(printed.Pdf);
        EncryptedFileResult encrypted = await encryption.EncryptAndSaveAsync(stream, fileName, cancellationToken);

        // Sursa se păstrează lângă document, criptată la fel: e singurul mod de a-l retipări mai
        // târziu identic, cu semnătura pe el.
        using var sourceStream = new MemoryStream(Encoding.UTF8.GetBytes(printed.Source));
        EncryptedFileResult encryptedSource = await encryption.EncryptAndSaveAsync(
            sourceStream, Path.ChangeExtension(fileName, ".tex"), cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userContext.UserId,
            CarId = rental.CarId,
            OriginalFileName = fileName,
            StoredFileName = fileName,
            ContentType = "application/pdf",
            Category = DocumentCategory.Other,
            Status = DocumentStatus.Verified,
            // Generat de noi, nu încărcat de client: nu trece prin verificarea AI și nu se cere
            // aprobat de nimeni.
            Origin = DocumentOrigin.SystemGenerated,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = printed.Pdf.Length,
            UploadedAtUtc = DateTime.UtcNow,
            AiStatus = DocumentAiStatus.None,
        };

        context.Documents.Add(document);

        var generated = new GeneratedDocument
        {
            Id = Guid.NewGuid(),
            RentalId = rental.Id,
            Type = command.Type.ToString(),
            Status = GeneratedDocumentStatus.Generated,
            Version = version,
            DocumentId = document.Id,
            SourceFilePath = encryptedSource.FilePath,
            SourceIv = encryptedSource.Iv,
            GeneratedAtUtc = DateTime.UtcNow,
        };

        context.GeneratedDocuments.Add(generated);

        VehicleTimeline.Record(
            context,
            rental.CarId,
            VehicleEventType.DocumentGenerated,
            $"{DocumentLabel(command.Type)} generat pentru {rental.PublicCode}",
            rental.Id);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new GeneratedDocumentDto(
            generated.Id,
            generated.Type,
            generated.Status.ToString(),
            generated.Version,
            generated.DocumentId,
            generated.SignedDocumentId,
            generated.GeneratedAtUtc,
            generated.SentAtUtc,
            generated.SentToEmail,
            generated.SignedAtUtc));
    }

    private static string DocumentLabel(RentalDocumentType type) => type switch
    {
        RentalDocumentType.RentalContract => "Contract",
        RentalDocumentType.HandoverProtocol => "Proces-verbal de predare",
        _ => "Proces-verbal de primire",
    };
}
