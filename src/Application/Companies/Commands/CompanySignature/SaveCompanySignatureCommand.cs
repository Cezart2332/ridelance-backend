using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Companies;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Companies.Commands.CompanySignature;

/// <summary>Salvează specimenul de semnătură al firmei.</summary>
/// <param name="SignatureImage">PNG-ul semnăturii, ca data-URL. Vine din aceeași pânză ca la PFA.</param>
public sealed record SaveCompanySignatureCommand(string SignatureImage) : ICommand<Guid>;

internal sealed class SaveCompanySignatureCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IFileEncryptionService encryption)
    : ICommandHandler<SaveCompanySignatureCommand, Guid>
{
    /// <summary>Aceeași limită ca la semnarea unui document.</summary>
    private const int MaxSignatureBytes = 2 * 1024 * 1024;

    public async Task<Result<Guid>> Handle(
        SaveCompanySignatureCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        CompanyProfile? profile = await context.CompanyProfiles
            .SingleOrDefaultAsync(p => p.UserId == userContext.UserId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<Guid>(Error.Problem(
                "CompanySignature.NoProfile",
                "Salvează întâi datele firmei, apoi semnătura."));
        }

        byte[] signature;
        try
        {
            string payload = command.SignatureImage;
            int comma = payload.IndexOf(',', StringComparison.Ordinal);
            signature = Convert.FromBase64String(comma >= 0 ? payload[(comma + 1)..] : payload);
        }
        catch (FormatException)
        {
            return Result.Failure<Guid>(
                Error.Problem("CompanySignature.Invalid", "Semnătura nu a putut fi citită."));
        }

        if (signature.Length == 0 || signature.Length > MaxSignatureBytes)
        {
            return Result.Failure<Guid>(
                Error.Problem("CompanySignature.Invalid", "Semnătura nu a putut fi citită."));
        }

        string fileName = "specimen-semnatura.png";
        using var stream = new MemoryStream(signature);
        EncryptedFileResult encrypted = await encryption.EncryptAndSaveAsync(
            stream, fileName, cancellationToken);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userContext.UserId,
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

        context.Documents.Add(document);

        // Specimenul vechi nu se șterge: documentele tipărite cu el trebuie să se poată retipări
        // identic, iar ele îl țin după id.
        profile.SignatureDocumentId = document.Id;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(document.Id);
    }
}

/// <summary>Scoate specimenul de pe profil. Documentele deja tipărite rămân neatinse.</summary>
public sealed record DeleteCompanySignatureCommand : ICommand;

internal sealed class DeleteCompanySignatureCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<DeleteCompanySignatureCommand>
{
    public async Task<Result> Handle(
        DeleteCompanySignatureCommand command, CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await context.CompanyProfiles
            .SingleOrDefaultAsync(p => p.UserId == userContext.UserId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure(Error.NotFound("CompanySignature.NoProfile", "Profilul firmei nu există."));
        }

        profile.SignatureDocumentId = null;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
