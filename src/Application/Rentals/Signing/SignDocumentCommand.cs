using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.Rentals;
using SharedKernel;
using Application.Rentals.Checks;
using Application.Rentals.Documents;
using Domain.Cars;

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
    IFileEncryptionService encryption,
    IRentalDocumentGenerator generator)
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

        // Documentul se retipărește cu semnătura pe linia chiriașului. Se face înainte de orice
        // modificare de stare: dacă tipărirea eșuează, semnarea nu se înregistrează pe jumătate, iar
        // chiriașul poate reîncerca de pe același link.
        Document? signedPdf = await PrintSignedAsync(request.GeneratedDocument, signature, cancellationToken);

        if (signedPdf is not null)
        {
            context.Documents.Add(signedPdf);
            request.GeneratedDocument.SignedDocumentId = signedPdf.Id;
        }

        request.UsedAtUtc = DateTime.UtcNow;
        request.SignatureImageDocumentId = image.Id;
        request.IpAddress = command.Context.IpAddress;
        request.UserAgent = command.Context.UserAgent;

        request.GeneratedDocument.Status = GeneratedDocumentStatus.Signed;
        request.GeneratedDocument.SignedAtUtc = DateTime.UtcNow;
        request.GeneratedDocument.ExternalSignatureRef = request.Id.ToString();

        VehicleTimeline.Record(
            context,
            request.GeneratedDocument.Rental.CarId,
            VehicleEventType.DocumentSigned,
            $"Document semnat de {request.GeneratedDocument.Rental.Tenant.Name} · {request.GeneratedDocument.Rental.PublicCode}",
            request.GeneratedDocument.RentalId);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Documentul retipărit cu semnătura pe el, sau nimic dacă sursa lui nu s-a păstrat.</summary>
    /// <remarks>
    /// Documentele generate înainte ca sursa să fie păstrată rămân semnabile — semnătura se
    /// înregistrează și se păstrează ca imagine — dar nu se pot retipări. Se rezolvă regenerându-le.
    /// </remarks>
    private async Task<Document?> PrintSignedAsync(
        GeneratedDocument generated, byte[] signature, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(generated.SourceFilePath) || string.IsNullOrEmpty(generated.SourceIv))
        {
            return null;
        }

        string source;
        using (Stream stream = await encryption.DecryptAndReadAsync(
            generated.SourceFilePath, generated.SourceIv, cancellationToken))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            source = await reader.ReadToEndAsync(cancellationToken);
        }

        string note = string.Create(
            CultureInfo.InvariantCulture,
            $"Semnat electronic la {DateTime.UtcNow.ToLocalTime():dd.MM.yyyy, ora HH:mm}");

        Dictionary<int, RentalSignature> signatures = new()
        {
            [RentalDocumentComposer.TenantSignatureSlot] = new RentalSignature(signature, note),
        };

        // Semnătura firmei se pune din nou, altfel varianta semnată ar pierde-o. Se ia după id-ul
        // reținut pe document, nu de pe profil: dacă proprietarul și-a schimbat între timp
        // specimenul, documentul semnat trebuie să poarte semnătura care era pe cel trimis.
        byte[]? companySignature = await StoredSignature.ReadAsync(
            context, encryption, generated.CompanySignatureDocumentId, cancellationToken);

        if (companySignature is not null)
        {
            signatures[RentalDocumentComposer.CompanySignatureSlot] =
                new RentalSignature(companySignature, string.Empty);
        }

        byte[] pdf = await generator.SignAsync(source, signatures, cancellationToken);

        string fileName =
            $"{generated.Rental.PublicCode}-{generated.Type}-v{generated.Version}-semnat.pdf";

        using var pdfStream = new MemoryStream(pdf);
        EncryptedFileResult encrypted = await encryption.EncryptAndSaveAsync(
            pdfStream, fileName, cancellationToken);

        return new Document
        {
            Id = Guid.NewGuid(),
            UserId = generated.Rental.OwnerUserId,
            CarId = generated.Rental.CarId,
            OriginalFileName = fileName,
            StoredFileName = fileName,
            ContentType = "application/pdf",
            Category = DocumentCategory.Other,
            Status = DocumentStatus.Verified,
            Origin = DocumentOrigin.SystemGenerated,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = pdf.Length,
            UploadedAtUtc = DateTime.UtcNow,
            AiStatus = DocumentAiStatus.None,
        };
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
