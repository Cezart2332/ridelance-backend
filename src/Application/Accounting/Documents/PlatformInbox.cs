using System.Security.Cryptography;
using Application.Abstractions.Ai;
using Application.Abstractions.Authentication;
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

/// <summary>Un fișier din încărcarea globală.</summary>
public sealed record InboxUploadFile(string FileName, string ContentType, byte[] Content);

/// <summary>Ce s-a întâmplat cu un fișier: alocat (cui și cum), în coadă sau refuzat.</summary>
public sealed record PlatformInboxResultDto(
    Guid? ItemId, string FileName, PlatformInboxStatus? Status, Guid? PfaId, string? PfaName, PlatformInboxMatch? MatchedBy, string Message);

public sealed record PlatformInboxItemDto(
    Guid Id,
    string FileName,
    string Period,
    PlatformInboxStatus Status,
    string? Reason,
    string? DetectedCui,
    Platform? Platform,
    PlatformDocumentType DocumentType,
    decimal? CommissionAmount,
    DateTime UploadedAt,
    Guid? PfaId,
    string? PfaName,
    PlatformInboxMatch? MatchedBy);

internal static class PlatformInboxErrors
{
    public static readonly Error NotFound = Error.NotFound("Accounting.InboxItemNotFound", "Documentul nu e în coada de alocare.");

    public static readonly Error AlreadyResolved = Error.Conflict("Accounting.InboxItemResolved", "Documentul a fost deja alocat sau respins.");

    public static readonly Error NoFiles = Error.Problem("Accounting.InboxNoFiles", "Alege cel puțin un fișier PDF.");
}

/// <summary>Clienții PFA și alocarea unui fișier la unul dintre ei.</summary>
internal static class PlatformInboxSupport
{
    public static async Task<List<InboxClient>> ClientsAsync(IApplicationDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.User.DeletedAtUtc == null)
            .Select(p => new { p.Id, p.Cui, p.LegalName, p.HolderName, p.FullName, p.User.FirstName, p.User.LastName })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(p => new InboxClient(
            p.Id,
            p.Cui,
            [.. new[] { p.LegalName, p.HolderName, p.FullName, $"{p.FirstName} {p.LastName}" }.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!)]))];
    }

    /// <summary>Fișierul devine documentul de platformă al clientului, în coada de extracție.</summary>
    public static async Task<Result<PlatformDocument>> AssignAsync(
        IApplicationDbContext db, PlatformInboxItem item, Guid pfaId, PlatformInboxMatch match, string reason, Guid userId, CancellationToken cancellationToken)
    {
        var pfa = await db.PfaRegistrations.AsNoTracking().Where(p => p.Id == pfaId).Select(p => new { p.Id, p.UserId }).SingleOrDefaultAsync(cancellationToken);
        if (pfa is null)
        {
            return Result.Failure<PlatformDocument>(AccountingErrors.PfaNotFound);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, pfa.Id, item.Period, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<PlatformDocument>(writable.Error);
        }

        // FindAsync vede și fișierul abia adăugat (încărcarea alocă înainte de salvare).
        Document file = await db.Documents.FindAsync([item.SourceDocumentId], cancellationToken)
            ?? throw new InvalidOperationException($"Fișierul {item.SourceDocumentId} lipsește.");
        PlatformDocument document = PlatformDocumentStore.Create(db, pfa.Id, pfa.UserId, item.Period, file, item.FileHash, userId);
        item.Status = PlatformInboxStatus.Assigned;
        item.MatchedBy = match;
        item.Reason = reason;
        item.PfaRegistrationId = pfa.Id;
        item.PlatformDocumentId = document.Id;
        item.ResolvedAtUtc = DateTime.UtcNow;
        item.ResolvedByUserId = match == PlatformInboxMatch.Manual ? userId : null;
        return document;
    }

    public static string NameOf(Domain.PfaRegistrations.PfaRegistration pfa) =>
        Pfas.PfaNames.Of(pfa.LegalName, pfa.HolderName, pfa.FullName, pfa.User?.FirstName, pfa.User?.LastName);
}

/// <summary>
/// <c>POST /accounting/platform-inbox</c> — încărcarea globală din „Clienți PFA”: rapoarte de venituri și
/// facturi de comision pentru mai mulți clienți deodată. Fiecare fișier se alocă după CUI-ul din text sau
/// după numele fișierului; un CUI valid care nu e al niciunui client se raportează; fără CUI, documentul se
/// citește și se caută după comision, altfel rămâne „De verificat”.
/// </summary>
public sealed record UploadPlatformInboxCommand(string Period, IReadOnlyList<InboxUploadFile> Files) : ICommand<IReadOnlyList<PlatformInboxResultDto>>;

internal sealed class UploadPlatformInboxCommandHandler(
    IApplicationDbContext db,
    IFileEncryptionService encryption,
    IPdfTextExtractor pdfText,
    IUserContext userContext,
    IOptions<AccountingOptions> options)
    : ICommandHandler<UploadPlatformInboxCommand, IReadOnlyList<PlatformInboxResultDto>>
{
    public async Task<Result<IReadOnlyList<PlatformInboxResultDto>>> Handle(UploadPlatformInboxCommand command, CancellationToken cancellationToken)
    {
        if (!PlatformDocumentSupport.IsValidPeriod(command.Period))
        {
            return Result.Failure<IReadOnlyList<PlatformInboxResultDto>>(AccountingErrors.InvalidPeriod);
        }

        if (command.Files.Count == 0)
        {
            return Result.Failure<IReadOnlyList<PlatformInboxResultDto>>(PlatformInboxErrors.NoFiles);
        }

        List<InboxClient> clients = await PlatformInboxSupport.ClientsAsync(db, cancellationToken);
        Dictionary<Guid, string> names = await db.PfaRegistrations.AsNoTracking().Include(p => p.User)
            .ToDictionaryAsync(p => p.Id, PlatformInboxSupport.NameOf, cancellationToken);
        HashSet<string> knownCuis = [.. clients.Select(c => PlatformInboxMatcher.NormalizeCui(c.Cui)).OfType<string>()];

        var results = new List<PlatformInboxResultDto>();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (InboxUploadFile upload in command.Files)
        {
            results.Add(await UploadAsync(command.Period, upload, clients, knownCuis, names, hashes, cancellationToken));
        }

        await db.SaveChangesAsync(cancellationToken);
        return results;
    }

    private async Task<PlatformInboxResultDto> UploadAsync(
        string defaultPeriod,
        InboxUploadFile upload,
        List<InboxClient> clients,
        HashSet<string> knownCuis,
        Dictionary<Guid, string> names,
        HashSet<string> hashes,
        CancellationToken cancellationToken)
    {
        bool pdf = upload.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) || upload.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        if (!pdf)
        {
            return new PlatformInboxResultDto(null, upload.FileName, null, null, null, null, "Doar fișiere PDF.");
        }

        if (upload.Content.Length > options.Value.MaxUploadBytes)
        {
            return new PlatformInboxResultDto(null, upload.FileName, null, null, null, null, AccountingErrors.FileTooLarge.Description);
        }

        string hash = Convert.ToHexStringLower(SHA256.HashData(upload.Content));
        var existing = await db.PlatformDocuments.AsNoTracking()
            .Where(d => d.FileHash == hash && d.DeletedAtUtc == null)
            .Select(d => new { d.PfaRegistrationId })
            .FirstOrDefaultAsync(cancellationToken);
        bool queued = await db.PlatformInboxItems.AnyAsync(
            i => i.FileHash == hash && i.Status != PlatformInboxStatus.Dismissed && i.Status != PlatformInboxStatus.Failed && i.Status != PlatformInboxStatus.Assigned, cancellationToken);
        if (existing is not null || queued || !hashes.Add(hash))
        {
            string where = existing is not null ? $" la {names.GetValueOrDefault(existing.PfaRegistrationId, "un client")}" : string.Empty;
            return new PlatformInboxResultDto(null, upload.FileName, null, existing?.PfaRegistrationId, null, null, $"Duplicat: fișierul e deja încărcat{where}.");
        }

        string period = PlatformInboxMatcher.PeriodFromFileName(upload.FileName) ?? defaultPeriod;
        string storedFileName = $"{Guid.NewGuid()}.pdf";
        using var buffer = new MemoryStream(upload.Content);
        EncryptedFileResult encrypted = await encryption.EncryptAndSaveAsync(buffer, storedFileName, cancellationToken);
        var file = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userContext.UserId,
            OriginalFileName = upload.FileName,
            StoredFileName = storedFileName,
            ContentType = "application/pdf",
            Category = DocumentCategory.Other,
            Status = DocumentStatus.Verified,
            Origin = DocumentOrigin.AccountingUpload,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = upload.Content.Length,
            UploadedAtUtc = DateTime.UtcNow,
            AiStatus = DocumentAiStatus.None,
        };
        db.Documents.Add(file);

        var item = new PlatformInboxItem
        {
            Id = Guid.NewGuid(),
            SourceDocumentId = file.Id,
            FileName = upload.FileName,
            FileHash = hash,
            Period = period,
            Status = PlatformInboxStatus.Matching,
            UploadedByUserId = userContext.UserId,
            UploadedAtUtc = DateTime.UtcNow,
        };
        db.PlatformInboxItems.Add(item);

        string? text = pdfText.ExtractText(upload.Content);
        InboxClient? client = PlatformInboxMatcher.ByCui(text, clients);
        PlatformInboxMatch match = PlatformInboxMatch.Cui;
        if (client is null)
        {
            client = PlatformInboxMatcher.ByFileName(upload.FileName, clients);
            match = PlatformInboxMatch.FileName;
        }

        if (client is not null)
        {
            string reason = match == PlatformInboxMatch.Cui ? $"CUI {client.Cui} din document" : "Numele fișierului";
            Result<PlatformDocument> assigned = await PlatformInboxSupport.AssignAsync(db, item, client.PfaId, match, reason, userContext.UserId, cancellationToken);
            if (assigned.IsSuccess)
            {
                return new PlatformInboxResultDto(item.Id, upload.FileName, item.Status, client.PfaId, names.GetValueOrDefault(client.PfaId), match, $"Alocat: {reason}.");
            }

            item.Status = PlatformInboxStatus.NeedsReview;
            item.Reason = assigned.Error.Description;
            return new PlatformInboxResultDto(item.Id, upload.FileName, item.Status, client.PfaId, names.GetValueOrDefault(client.PfaId), null, assigned.Error.Description);
        }

        // Un CUI marcat ca atare, valid, care nu e al niciunui client: se raportează în Admin.
        List<string> unknown = [.. PlatformInboxMatcher.LabelledCuisIn(text).Where(cui => !knownCuis.Contains(cui))];
        if (unknown.Count > 0)
        {
            item.Status = PlatformInboxStatus.UnknownCui;
            item.DetectedCui = unknown[0];
            item.Reason = $"CUI {unknown[0]} nu aparține niciunui client PFA.";
            return new PlatformInboxResultDto(item.Id, upload.FileName, item.Status, null, null, null, item.Reason);
        }

        item.Reason = "Fără CUI în document: se citește și se caută după comision.";
        return new PlatformInboxResultDto(item.Id, upload.FileName, item.Status, null, null, null, item.Reason);
    }
}

/// <summary>
/// Pasul al doilea, în coada de extracție: documentul fără CUI se citește, apoi se caută clientul după
/// comision — raportul Bolt ↔ factura de comision Bolt cu același comision în aceeași lună (la Uber, suma
/// facturilor săptămânale). Cât timp documentele perechii se citesc încă, așteaptă; fără potrivire unică,
/// „De verificat”.
/// </summary>
public sealed record RunPlatformInboxMatchingCommand(Guid ItemId) : ICommand;

internal sealed class RunPlatformInboxMatchingCommandHandler(
    IApplicationDbContext db,
    IFileEncryptionService encryption,
    IPdfTextExtractor pdfText,
    IDocumentExtractor extractor)
    : ICommandHandler<RunPlatformInboxMatchingCommand>
{
    /// <summary>Cât așteaptă perechea încă necitită înainte de „De verificat”.</summary>
    public static readonly TimeSpan PairWait = TimeSpan.FromMinutes(15);

    public async Task<Result> Handle(RunPlatformInboxMatchingCommand command, CancellationToken cancellationToken)
    {
        PlatformInboxItem? item = await db.PlatformInboxItems.SingleOrDefaultAsync(i => i.Id == command.ItemId, cancellationToken);
        if (item is not { Status: PlatformInboxStatus.Matching })
        {
            return Result.Success();
        }

        // O singură citire: la reîncercări se folosesc câmpurile salvate.
        if (item.DocumentType == PlatformDocumentType.Unknown)
        {
            Document file = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == item.SourceDocumentId, cancellationToken);
            byte[] bytes;
            await using (Stream stream = await encryption.DecryptAndReadAsync(file.EncryptedFilePath, file.EncryptionIv, cancellationToken))
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, cancellationToken);
                bytes = buffer.ToArray();
            }

            Result<DocumentExtractionResult> read = await extractor.ExtractAsync(
                new DocumentExtractionRequest(bytes, file.ContentType, file.OriginalFileName, pdfText.ExtractText(bytes), item.Period), cancellationToken);
            if (read.IsFailure || read.Value.DocumentType == PlatformDocumentType.Unknown)
            {
                item.Status = PlatformInboxStatus.NeedsReview;
                item.Reason = read.IsFailure ? $"De verificat: {read.Error.Description}" : "De verificat: tipul documentului nu a fost recunoscut.";
                await db.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }

            item.DocumentType = read.Value.DocumentType;
            item.Platform = read.Value.Platform;
            item.CommissionAmount = read.Value.Fields.CommissionAmount is { } commission ? Math.Abs(commission) : null;
            item.PeriodFrom = read.Value.Fields.PeriodFrom;
            item.PeriodTo = read.Value.Fields.PeriodTo;

            // Citirea AI (și OCR-ul paginilor scanate) dă destinatarul: CUI-ul, apoi numele, înaintea comisionului.
            if (await ByCustomerAsync(item, read.Value, cancellationToken))
            {
                await db.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
        }

        PlatformDocumentType pair = item.DocumentType == PlatformDocumentType.PlatformReport ? PlatformDocumentType.CommissionInvoice : PlatformDocumentType.PlatformReport;
        var candidates = await db.PlatformDocuments.AsNoTracking()
            .Where(d => d.DeletedAtUtc == null && d.Platform == item.Platform && d.DocumentType == pair && d.Period == item.Period)
            .Select(d => new
            {
                d.PfaRegistrationId,
                d.Status,
                Commission = d.Extractions.Where(e => e.IsCurrent).Select(e => e.CommissionAmount).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        bool pending = await db.PlatformDocuments.AnyAsync(
            d => d.DeletedAtUtc == null && d.Period == item.Period && d.Status == PlatformDocumentStatus.Extracting, cancellationToken);

        List<Guid> matches = item.CommissionAmount is { } amount
            ? [.. candidates
                .GroupBy(c => c.PfaRegistrationId)
                .Where(g => g.Any(c => c.Commission is { } one && Math.Abs(Math.Abs(one) - amount) < 0.01m) ||
                            Math.Abs(g.Sum(c => Math.Abs(c.Commission ?? 0)) - amount) < 0.01m)
                .Select(g => g.Key)]
            : [];

        if (matches.Count == 1)
        {
            await AssignOrReviewAsync(
                item, matches[0], PlatformInboxMatch.Commission,
                $"Comision {AccountingJson.Amount(item.CommissionAmount!.Value)} lei, ca în {(pair == PlatformDocumentType.CommissionInvoice ? "factura de comision" : "raportul")} clientului",
                cancellationToken);
        }
        else if (pending && DateTime.UtcNow - item.UploadedAtUtc < PairWait)
        {
            // Perechea se citește încă: reîncercare la trecerea următoare a cozii.
        }
        else
        {
            item.Status = PlatformInboxStatus.NeedsReview;
            item.Reason = matches.Count > 1
                ? "De verificat: același comision la mai mulți clienți."
                : "De verificat: fără CUI și fără comision potrivit cu un client.";
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Alocarea găsită; dacă luna clientului e închisă sau clientul e inactiv, „De verificat” cu motivul.</summary>
    private async Task AssignOrReviewAsync(PlatformInboxItem item, Guid pfaId, PlatformInboxMatch match, string reason, CancellationToken cancellationToken)
    {
        Result<PlatformDocument> assigned = await PlatformInboxSupport.AssignAsync(db, item, pfaId, match, reason, item.UploadedByUserId, cancellationToken);
        if (assigned.IsFailure)
        {
            item.Status = PlatformInboxStatus.NeedsReview;
            item.PfaRegistrationId = pfaId;
            item.Reason = $"De verificat: {assigned.Error.Description}";
        }
    }

    /// <summary>Destinatarul citit de AI: alocă după CUI sau nume ori raportează CUI-ul străin; <c>false</c> dacă nu decide nimic.</summary>
    private async Task<bool> ByCustomerAsync(PlatformInboxItem item, DocumentExtractionResult read, CancellationToken cancellationToken)
    {
        if (read.CustomerTaxId is null && read.CustomerName is null)
        {
            return false;
        }

        List<InboxClient> clients = await PlatformInboxSupport.ClientsAsync(db, cancellationToken);
        if (PlatformInboxMatcher.ByCui(read.CustomerTaxId, clients) is { } byCui)
        {
            await AssignOrReviewAsync(item, byCui.PfaId, PlatformInboxMatch.Cui, $"CUI {byCui.Cui} citit din document", cancellationToken);
            return true;
        }

        if (PlatformInboxMatcher.ByName(read.CustomerName, clients) is { } byName)
        {
            await AssignOrReviewAsync(item, byName.PfaId, PlatformInboxMatch.Name, $"Numele „{read.CustomerName}” din document", cancellationToken);
            return true;
        }

        if (PlatformInboxMatcher.ValidCuisIn(read.CustomerTaxId) is [var stranger, ..])
        {
            item.Status = PlatformInboxStatus.UnknownCui;
            item.DetectedCui = stranger;
            item.Reason = $"CUI {stranger} nu aparține niciunui client PFA.";
            return true;
        }

        return false;
    }
}

/// <summary><c>GET /accounting/platform-inbox</c> — documentele nealocate (de verificat, CUI necunoscut, în căutare).</summary>
public sealed record GetPlatformInboxQuery(bool OnlyOpen = true) : IQuery<IReadOnlyList<PlatformInboxItemDto>>;

internal sealed class GetPlatformInboxQueryHandler(IApplicationDbContext db) : IQueryHandler<GetPlatformInboxQuery, IReadOnlyList<PlatformInboxItemDto>>
{
    public async Task<Result<IReadOnlyList<PlatformInboxItemDto>>> Handle(GetPlatformInboxQuery query, CancellationToken cancellationToken)
    {
        List<PlatformInboxItem> items = await db.PlatformInboxItems.AsNoTracking()
            .Where(i => !query.OnlyOpen || i.Status == PlatformInboxStatus.Matching || i.Status == PlatformInboxStatus.NeedsReview ||
                        i.Status == PlatformInboxStatus.UnknownCui || i.Status == PlatformInboxStatus.Failed)
            .OrderByDescending(i => i.UploadedAtUtc)
            .Take(500)
            .ToListAsync(cancellationToken);
        List<Guid> ids = [.. items.Select(i => i.PfaRegistrationId).OfType<Guid>().Distinct()];
        Dictionary<Guid, string> names = await db.PfaRegistrations.AsNoTracking().Include(p => p.User)
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, PlatformInboxSupport.NameOf, cancellationToken);
        return items.Select(i => new PlatformInboxItemDto(
            i.Id, i.FileName, i.Period, i.Status, i.Reason, i.DetectedCui, i.Platform, i.DocumentType, i.CommissionAmount, i.UploadedAtUtc,
            i.PfaRegistrationId, i.PfaRegistrationId is { } id ? names.GetValueOrDefault(id) : null, i.MatchedBy)).ToList();
    }
}

/// <summary><c>POST /accounting/platform-inbox/{id}/assign</c> — Adminul alege clientul.</summary>
public sealed record AssignPlatformInboxItemCommand(Guid ItemId, Guid PfaId) : ICommand;

internal sealed class AssignPlatformInboxItemCommandHandler(IApplicationDbContext db, IUserContext userContext) : ICommandHandler<AssignPlatformInboxItemCommand>
{
    public async Task<Result> Handle(AssignPlatformInboxItemCommand command, CancellationToken cancellationToken)
    {
        PlatformInboxItem? item = await db.PlatformInboxItems.SingleOrDefaultAsync(i => i.Id == command.ItemId, cancellationToken);
        if (item is null)
        {
            return Result.Failure(PlatformInboxErrors.NotFound);
        }

        if (item.Status is PlatformInboxStatus.Assigned or PlatformInboxStatus.Dismissed)
        {
            return Result.Failure(PlatformInboxErrors.AlreadyResolved);
        }

        Result<PlatformDocument> assigned = await PlatformInboxSupport.AssignAsync(
            db, item, command.PfaId, PlatformInboxMatch.Manual, "Alocat de Admin", userContext.UserId, cancellationToken);
        if (assigned.IsFailure)
        {
            return assigned;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary><c>POST /accounting/platform-inbox/{id}/dismiss</c> — documentul nu ține de niciun client.</summary>
public sealed record DismissPlatformInboxItemCommand(Guid ItemId) : ICommand;

internal sealed class DismissPlatformInboxItemCommandHandler(IApplicationDbContext db, IUserContext userContext) : ICommandHandler<DismissPlatformInboxItemCommand>
{
    public async Task<Result> Handle(DismissPlatformInboxItemCommand command, CancellationToken cancellationToken)
    {
        PlatformInboxItem? item = await db.PlatformInboxItems.SingleOrDefaultAsync(i => i.Id == command.ItemId, cancellationToken);
        if (item is null)
        {
            return Result.Failure(PlatformInboxErrors.NotFound);
        }

        if (item.Status is PlatformInboxStatus.Assigned or PlatformInboxStatus.Dismissed)
        {
            return Result.Failure(PlatformInboxErrors.AlreadyResolved);
        }

        item.Status = PlatformInboxStatus.Dismissed;
        item.ResolvedAtUtc = DateTime.UtcNow;
        item.ResolvedByUserId = userContext.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
