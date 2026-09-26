using System.Globalization;
using Application.Abstractions.Ai;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.Documents;
using Domain.Accounting;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Ledger;

/// <summary>Un fișier încărcat în ledger: PDF sau imagine.</summary>
public sealed record LedgerUpload(string FileName, string ContentType, byte[] Content)
{
    private static readonly string[] Extensions = [".pdf", ".png", ".jpg", ".jpeg"];

    public bool IsAllowed =>
        Extensions.Any(extension => FileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) ||
        ContentType is "application/pdf" or "image/png" or "image/jpeg";
}

internal static class LedgerUploadErrors
{
    public static readonly Error FileType = Error.Problem("Accounting.UploadFileType", "Se acceptă PDF sau imagine (PNG, JPEG).");

    public static readonly Error ZUnreadable = Error.Problem(
        "Accounting.ZReportUnreadable",
        "Raportul Z nu a putut fi citit complet (data, numărul sau totalul lipsesc). Adaugă încasarea manual.");

    public static readonly Error CashNotActive = Error.Conflict(
        "Accounting.CashNotActive",
        "Rapoartele Z se pot încărca doar cu casa de marcat activă la data raportului.");

    public static Error ZDuplicate(string number) => Error.Conflict("Accounting.ZReportDuplicate", $"Raportul Z nr. {number} e deja înregistrat.");

    public static async Task<Result> CheckAsync(IApplicationDbContext db, Guid pfaId, LedgerUpload file, AccountingOptions options, CancellationToken cancellationToken)
    {
        if (!file.IsAllowed)
        {
            return Result.Failure(FileType);
        }

        if (file.Content.Length > options.MaxUploadBytes)
        {
            return Result.Failure(AccountingErrors.FileTooLarge);
        }

        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == pfaId, cancellationToken))
        {
            return Result.Failure(AccountingErrors.PfaNotFound);
        }

        // Doar dosarul activ; luna se verifică după ce se citește data documentului.
        return await PlatformDocumentSupport.EnsureWritableAsync(db, pfaId, string.Empty, cancellationToken);
    }
}

/// <summary><c>POST /accounting/pfas/{pfaId}/expense-documents</c> — multipart(file).</summary>
public sealed record UploadExpenseDocumentCommand(Guid PfaId, LedgerUpload File) : ICommand<ExpenseDocumentUploadResult>;

/// <summary>
/// Un document de cheltuială (spec contabilitate B6): se păstrează, se citește (comerciant, CUI,
/// dată, total, produse) și se propune plata din bancă pe care o justifică — aceeași sumă, în jurul
/// datei. Potrivirea o confirmă utilizatorul (<c>PATCH ledger</c> cu <c>sourceDocumentId</c>).
/// </summary>
internal sealed class UploadExpenseDocumentCommandHandler(
    IApplicationDbContext db,
    DeclarationFiles files,
    IReceiptExtractor extractor,
    IUserContext userContext,
    IOptions<AccountingOptions> options)
    : ICommandHandler<UploadExpenseDocumentCommand, ExpenseDocumentUploadResult>
{
    public async Task<Result<ExpenseDocumentUploadResult>> Handle(UploadExpenseDocumentCommand command, CancellationToken cancellationToken)
    {
        Result allowed = await LedgerUploadErrors.CheckAsync(db, command.PfaId, command.File, options.Value, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<ExpenseDocumentUploadResult>(allowed.Error);
        }

        Document document = await files.StoreAsync(
            command.PfaId, command.File.Content, command.File.FileName, command.File.ContentType, cancellationToken, DocumentOrigin.AccountingUpload);
        Result<ExpenseReceiptReading> read = await extractor.ReadExpenseAsync(
            new ReceiptExtractionRequest(command.File.Content, command.File.ContentType, command.File.FileName), cancellationToken);
        // O citire eșuată nu pierde documentul: rămâne încărcat, fără propunere.
        ExpenseReceiptReading reading = read.IsSuccess ? read.Value : new ExpenseReceiptReading(null, null, null, null, []);

        var expense = new ExpenseDocument
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = command.PfaId,
            DocumentId = document.Id,
            Merchant = reading.Merchant,
            MerchantCui = reading.MerchantCui,
            Date = reading.Date,
            Total = reading.Total,
            ItemsJson = AccountingJson.Serialize(reading.Items),
            UploadedByUserId = userContext.UserId,
            UploadedAtUtc = DateTime.UtcNow,
        };
        db.ExpenseDocuments.Add(expense);
        AccountingAudit.Record(
            db, command.PfaId, nameof(ExpenseDocument), expense.Id, "UPLOAD", null,
            new { command.File.FileName, reading.Merchant, reading.Date, reading.Total }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);

        LedgerEntryDto? proposed = await ProposeAsync(command.PfaId, reading, cancellationToken);
        return new ExpenseDocumentUploadResult(
            document.Id,
            new ExpenseDocumentExtracted(reading.Merchant, reading.MerchantCui, reading.Date, reading.Total, reading.Items),
            proposed);
    }

    /// <summary>Plata din bancă fără document, cu aceeași sumă, cea mai apropiată de dată; la egalitate, cea a comerciantului.</summary>
    private async Task<LedgerEntryDto?> ProposeAsync(Guid pfaId, ExpenseReceiptReading reading, CancellationToken cancellationToken)
    {
        if (reading.Total is not { } total || reading.Date is not { } date)
        {
            return null;
        }

        int days = options.Value.ExpenseMatchDays;
        DateOnly from = date.AddDays(-days);
        DateOnly to = date.AddDays(days);
        HashSet<string> closed = await LedgerSupport.ClosedPeriodsAsync(db, pfaId, cancellationToken);
        List<LedgerEntry> candidates = await db.LedgerEntries.AsNoTracking()
            .Where(LedgerSupport.UndocumentedBankExpense(pfaId))
            .Where(e => e.Amount >= -total - 0.01m && e.Amount <= -total + 0.01m && e.Date >= from && e.Date <= to)
            .ToListAsync(cancellationToken);

        string? merchant = reading.Merchant?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        LedgerEntry? best = candidates
            .Where(e => !closed.Contains(e.AccountingPeriod))
            .OrderBy(e => Math.Abs(e.Date.DayNumber - date.DayNumber))
            .ThenByDescending(e => merchant is not null && (e.Counterparty ?? string.Empty).Contains(merchant, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        return best is null ? null : await LedgerSupport.DtoAsync(db, best.Id, cancellationToken);
    }
}

/// <summary><c>POST /accounting/pfas/{pfaId}/z-reports</c> — multipart(file).</summary>
public sealed record UploadZReportCommand(Guid PfaId, LedgerUpload File) : ICommand<ZReportUploadResult>;

/// <summary>
/// Raportul Z (spec contabilitate B6): citire (dată, număr, total), apoi încasarea în numerar în
/// ledger (<c>INCOME</c> / <c>CASH</c> / <c>CASH_Z</c>), de verificat de utilizator. Doar cu casa de
/// marcat activă la data raportului și într-o lună deschisă; un număr Z se înregistrează o dată.
/// </summary>
internal sealed class UploadZReportCommandHandler(
    IApplicationDbContext db,
    DeclarationFiles files,
    IReceiptExtractor extractor,
    IUserContext userContext,
    IOptions<AccountingOptions> options)
    : ICommandHandler<UploadZReportCommand, ZReportUploadResult>
{
    public async Task<Result<ZReportUploadResult>> Handle(UploadZReportCommand command, CancellationToken cancellationToken)
    {
        Result allowed = await LedgerUploadErrors.CheckAsync(db, command.PfaId, command.File, options.Value, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<ZReportUploadResult>(allowed.Error);
        }

        Result<ZReportReading> read = await extractor.ReadZReportAsync(
            new ReceiptExtractionRequest(command.File.Content, command.File.ContentType, command.File.FileName), cancellationToken);
        if (read.IsFailure || read.Value is not { Date: { } date, Total: { } total, ZNumber: { Length: > 0 } number } || total <= 0)
        {
            return Result.Failure<ZReportUploadResult>(LedgerUploadErrors.ZUnreadable);
        }

        CashRegisterState? cash = await db.CashRegisterStates.AsNoTracking().FirstOrDefaultAsync(c => c.PfaRegistrationId == command.PfaId, cancellationToken);
        if (cash is not { Status: CashRegisterStatus.Active, ActivationDate: { } activated } || activated > date)
        {
            return Result.Failure<ZReportUploadResult>(LedgerUploadErrors.CashNotActive);
        }

        Result open = await PlatformDocumentSupport.EnsureWritableAsync(db, command.PfaId, LedgerSupport.PeriodOf(date), cancellationToken);
        if (open.IsFailure)
        {
            return Result.Failure<ZReportUploadResult>(open.Error);
        }

        string zNumber = number.Trim();
        if (await db.ZReports.AnyAsync(z => z.PfaRegistrationId == command.PfaId && z.ZNumber == zNumber, cancellationToken))
        {
            return Result.Failure<ZReportUploadResult>(LedgerUploadErrors.ZDuplicate(zNumber));
        }

        Document document = await files.StoreAsync(
            command.PfaId, command.File.Content, command.File.FileName, command.File.ContentType, cancellationToken, DocumentOrigin.AccountingUpload);
        LedgerEntry entry = LedgerSupport.New(
            command.PfaId,
            date,
            LedgerSource.CashZ,
            $"{command.PfaId:N}:Z{zNumber}",
            $"Raport Z nr. {zNumber}",
            null,
            $"Încasări numerar, raport Z nr. {zNumber}",
            LedgerTransactionType.Income,
            PaymentMethod.Cash,
            total,
            "RON",
            // Câmpurile citite le confirmă utilizatorul, prin „Verifică”.
            LedgerEntryStatus.NeedsReview,
            new HashSet<string>());
        entry.SourceDocumentId = document.Id;
        entry.CreatedByUserId = userContext.UserId;
        var report = new ZReport
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = command.PfaId,
            Date = date,
            ZNumber = zNumber,
            Total = total,
            DocumentId = document.Id,
            LedgerEntryId = entry.Id,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.LedgerEntries.Add(entry);
        db.ZReports.Add(report);
        AccountingAudit.Record(
            db, command.PfaId, nameof(ZReport), report.Id, "UPLOAD", null,
            new { date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), zNumber, total }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);

        return new ZReportUploadResult(new ZReportExtracted(date, zNumber, total), await LedgerSupport.DtoAsync(db, entry.Id, cancellationToken));
    }
}
