using Application.Abstractions.Ai;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Documents;

/// <summary>Citirea unui document din coadă (rulată de jobul de extracție).</summary>
public sealed record RunPlatformDocumentExtractionCommand(Guid PlatformDocumentId) : ICommand;

/// <summary>
/// Extracția (spec B1): textul PDF (PdfPig), apelul la model cu schemă JSON strictă, o
/// <see cref="DocumentExtraction"/> nouă, apoi verificările deterministe decid
/// <c>NEEDS_REVIEW</c> sau <c>PENDING_CONFIRMATION</c>. O eroare lasă documentul
/// <c>EXTRACTION_FAILED</c>, cu mesajul.
/// </summary>
internal sealed class RunPlatformDocumentExtractionCommandHandler(
    IApplicationDbContext db,
    IFileEncryptionService encryption,
    IPdfTextExtractor pdfText,
    IDocumentExtractor extractor,
    IOptions<AccountingOptions> options)
    : ICommandHandler<RunPlatformDocumentExtractionCommand>
{
    public async Task<Result> Handle(RunPlatformDocumentExtractionCommand command, CancellationToken cancellationToken)
    {
        PlatformDocument? document = await db.PlatformDocuments
            .Include(d => d.SourceDocument)
            .SingleOrDefaultAsync(d => d.Id == command.PlatformDocumentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure(AccountingErrors.DocumentNotFound);
        }

        if (document.Status != PlatformDocumentStatus.Extracting)
        {
            return Result.Success();
        }

        Document file = document.SourceDocument;
        byte[] bytes;
        await using (Stream stream = await encryption.DecryptAndReadAsync(file.EncryptedFilePath, file.EncryptionIv, cancellationToken))
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        string? text = pdfText.ExtractText(bytes);
        document.PdfText = text;
        document.HasTextLayer = text is not null;

        Result<DocumentExtractionResult> extracted = await extractor.ExtractAsync(
            new DocumentExtractionRequest(bytes, file.ContentType, file.OriginalFileName, text, document.Period),
            cancellationToken);

        if (extracted.IsFailure)
        {
            document.Status = PlatformDocumentStatus.ExtractionFailed;
            document.ExtractionError = extracted.Error.Description;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure(extracted.Error);
        }

        DocumentExtractionResult result = extracted.Value;
        document.DocumentType = result.DocumentType;
        document.Platform = result.Platform;
        document.ExtractionError = null;
        file.Category = PlatformDocumentSupport.CategoryFor(result.Platform, result.DocumentType);

        if (result.DocumentType == PlatformDocumentType.Unknown)
        {
            document.Status = PlatformDocumentStatus.ExtractionFailed;
            document.ExtractionError = "Tipul documentului nu a putut fi recunoscut. Încarcă factura de comision sau raportul lunar Bolt/Uber.";
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        List<DocumentExtraction> previous = await db.DocumentExtractions
            .Where(e => e.PlatformDocumentId == document.Id)
            .ToListAsync(cancellationToken);
        previous.ForEach(e => e.IsCurrent = false);

        IReadOnlyList<DocumentCheck> checks = await PlatformDocumentSupport.RunChecksAsync(db, document, result.Fields, options.Value, cancellationToken);

        var extraction = new DocumentExtraction
        {
            Id = Guid.NewGuid(),
            PlatformDocumentId = document.Id,
            Version = previous.Count == 0 ? 1 : previous.Max(e => e.Version) + 1,
            IsCurrent = true,
            SourceSnippetsJson = AccountingJson.Serialize(result.SourceSnippets),
            ChecksResultJson = AccountingJson.Serialize(checks),
            ModelConfidence = result.Confidence,
            ModelId = result.ModelId,
            PromptVersion = result.PromptVersion,
            CreatedAtUtc = DateTime.UtcNow,
        };
        PlatformDocumentSupport.Apply(extraction, result.Fields);
        db.DocumentExtractions.Add(extraction);

        document.Status = PlatformDocumentSupport.StatusAfter(checks);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
