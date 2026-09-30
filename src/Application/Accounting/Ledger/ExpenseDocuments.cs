using System.Globalization;
using Application.Abstractions.Ai;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Ledger;

/// <summary>
/// Împărțirea unui bon în activitate și personal (spec flux contabil R30). Doar o propunere: omul o
/// confirmă. La benzinărie, tot ce nu ține de mașină (cafea, gustări) e propus personal; la ceilalți
/// comercianți (service, spălătorie), tot bonul e propus pentru activitate.
/// </summary>
public static class ReceiptSplit
{
    /// <summary>Ce ține de mașină pe un bon de benzinărie, pe textul normalizat.</summary>
    private static readonly string[] VehicleWords =
    [
        "MOTORINA", "BENZINA", "DIESEL", "GPL", "ADBLUE", "CARBURANT", "EFIX", "EURO", "SPALARE", "SPALATORIE",
        "ULEI", "ANTIGEL", "PARBRIZ", "ROVINIETA", "PARCARE", "TAXA POD", "AER", "ASPIRARE", "PNEU",
    ];

    public static IReadOnlyList<ExpenseLineDto> Suggest(IReadOnlyList<ReceiptLine> lines, bool fuelMerchant) =>
        [.. lines.Select(line => new ExpenseLineDto(
            line.Name,
            line.Amount,
            fuelMerchant && !VehicleWords.Any(word => CounterpartyRules.Normalize(line.Name).Contains(word, StringComparison.Ordinal))))];

    /// <summary>Partea personală: suma liniilor personale, cel mult totalul.</summary>
    public static decimal PersonalAmount(IEnumerable<ExpenseLineDto> lines, decimal total) =>
        Math.Min(LedgerInvariants.Round(lines.Where(line => line.Personal).Sum(line => line.Amount ?? 0)), Math.Abs(total));

    /// <summary>
    /// Cât de sigur e documentul pentru calculul fiscal (R31–R33): CUI-ul PFA-ului pe bon = sigur; fără CUI,
    /// după natura cheltuielii (fără categorie = de verificat); alt CUI = de verificat, nu intră în calcul.
    /// </summary>
    public static ReconciliationStatus Confidence(string? beneficiaryCui, string? pfaCui, string? category)
    {
        string buyer = Digits(beneficiaryCui);
        if (buyer.Length > 0)
        {
            return buyer == Digits(pfaCui) ? ReconciliationStatus.Matched : ReconciliationStatus.NeedsReview;
        }

        return category is null ? ReconciliationStatus.NeedsReview : ReconciliationStatus.Matched;
    }

    public static string Digits(string? value) => new([.. (value ?? string.Empty).Where(char.IsAsciiDigit)]);
}

/// <summary>Cum a fost plătit bonul (R34): din contul conectat, numerar sau card / cont neconectat.</summary>
public enum ExpensePaymentChoice
{
    Bank = 0,
    Cash = 1,
    Manual = 2,
}

/// <summary><c>POST /accounting/pfas/{pfaId}/expense-documents/{id}/confirm</c></summary>
/// <param name="LedgerEntryId">Plata din bancă, la <see cref="ExpensePaymentChoice.Bank"/>.</param>
/// <param name="PersonalAmount">Partea personală confirmată; lipsă = propunerea din liniile bonului.</param>
/// <param name="Category">Categoria cheltuielii; lipsă = cea a comerciantului, dacă se recunoaște.</param>
public sealed record ConfirmExpenseDocumentCommand(
    Guid PfaId,
    Guid ExpenseDocumentId,
    ExpensePaymentChoice Payment,
    Guid? LedgerEntryId,
    decimal? PersonalAmount,
    string? Category) : ICommand<LedgerEntryDto>;

internal static class ExpenseDocumentErrors
{
    public static readonly Error NotFound = Error.NotFound("Accounting.ExpenseDocumentNotFound", "Documentul de cheltuială nu există.");

    public static readonly Error AlreadyConfirmed = Error.Conflict("Accounting.ExpenseDocumentConfirmed", "Documentul e deja asociat unei plăți.");

    public static readonly Error Unreadable = Error.Problem(
        "Accounting.ExpenseDocumentUnreadable", "Data și totalul documentului lipsesc; completează-le înainte de confirmare.");

    public static readonly Error PaymentRequired = Error.Problem("Accounting.PaymentRequired", "Alege plata din bancă pe care o justifică documentul.");

    public static readonly Error PaymentTaken = Error.Conflict("Accounting.PaymentTaken", "Plata are deja un document justificativ.");

    public static Error TotalMismatch(decimal total, decimal paid) => Error.Problem(
        "Accounting.TotalMismatch",
        $"Totalul documentului ({AccountingJson.Amount(total)} lei) nu e suma plății ({AccountingJson.Amount(paid)} lei).");
}

/// <summary>
/// Confirmarea unui bon (spec flux contabil R30–R35). Cu plata din bancă, bonul devine documentul ei
/// (o singură plată). Plătit numerar sau cu card / cont neconectat, bonul creează plata: numerar în
/// casă, respectiv canalul <c>MANUAL</c>, fără nicio tranzacție inventată în contul conectat (R35).
/// </summary>
internal sealed class ConfirmExpenseDocumentCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<ConfirmExpenseDocumentCommand, LedgerEntryDto>
{
    public async Task<Result<LedgerEntryDto>> Handle(ConfirmExpenseDocumentCommand command, CancellationToken cancellationToken)
    {
        ExpenseDocument? document = await db.ExpenseDocuments
            .SingleOrDefaultAsync(d => d.Id == command.ExpenseDocumentId && d.PfaRegistrationId == command.PfaId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<LedgerEntryDto>(ExpenseDocumentErrors.NotFound);
        }

        if (document.LedgerEntryId is not null)
        {
            return Result.Failure<LedgerEntryDto>(ExpenseDocumentErrors.AlreadyConfirmed);
        }

        if (document.Total is not { } total || total <= 0 || document.Date is not { } date)
        {
            return Result.Failure<LedgerEntryDto>(ExpenseDocumentErrors.Unreadable);
        }

        string? pfaCui = await db.PfaRegistrations.Where(p => p.Id == command.PfaId).Select(p => p.Cui).SingleOrDefaultAsync(cancellationToken);
        LedgerRules rules = await LedgerSupport.RulesAsync(db, command.PfaId, cancellationToken);
        IReadOnlyList<ExpenseLineDto> lines = AccountingJson.Deserialize<List<ExpenseLineDto>>(document.LinesJson, []) ?? [];
        decimal personal = LedgerInvariants.Round(command.PersonalAmount ?? ReceiptSplit.PersonalAmount(lines, total));

        LedgerEntry entry;
        if (command.Payment == ExpensePaymentChoice.Bank)
        {
            Result<LedgerEntry> payment = await BankPaymentAsync(command, total, cancellationToken);
            if (payment.IsFailure)
            {
                return Result.Failure<LedgerEntryDto>(payment.Error);
            }

            entry = payment.Value;
        }
        else
        {
            string period = LedgerSupport.PeriodOf(date);
            Result open = await PlatformDocumentSupport.EnsureWritableAsync(db, command.PfaId, period, cancellationToken);
            if (open.IsFailure)
            {
                return Result.Failure<LedgerEntryDto>(open.Error);
            }

            string label = document.Number is { Length: > 0 } number ? $"Bon fiscal nr. {number}" : $"Bon fiscal {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}";
            entry = LedgerSupport.New(
                command.PfaId, date, LedgerSource.Upload, $"{document.Id:N}:payment", label, document.Merchant,
                document.Merchant ?? "Cheltuială cu bon", LedgerTransactionType.Expense,
                command.Payment == ExpensePaymentChoice.Cash ? PaymentMethod.Cash : PaymentMethod.Manual,
                -total, "RON", LedgerEntryStatus.AutoImported, new HashSet<string>());
            entry.CreatedByUserId = userContext.UserId;
            db.LedgerEntries.Add(entry);
        }

        entry.SourceDocumentId = document.DocumentId;
        entry.PersonalAmount = personal;
        entry.Category = command.Category
            ?? entry.Category
            ?? DeductibilityService.Classify(rules.Categories, date, document.Merchant)?.Category;
        entry.ReconciliationStatus = ReceiptSplit.Confidence(document.BeneficiaryCui, pfaCui, entry.Category);
        DeductibilityService.Resolve(entry, rules);
        document.LedgerEntryId = entry.Id;

        Result valid = await LedgerSupport.ValidateAsync(db, entry, cancellationToken);
        if (valid.IsFailure)
        {
            return Result.Failure<LedgerEntryDto>(valid.Error);
        }

        AccountingAudit.Record(
            db, command.PfaId, nameof(ExpenseDocument), document.Id, "CONFIRM", null,
            new { payment = command.Payment.ToString(), ledgerEntryId = entry.Id, personal, entry.Category, entry.ReconciliationStatus }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await LedgerSupport.DtoAsync(db, entry.Id, cancellationToken);
    }

    /// <summary>R30: plata din bancă fără document, cu exact totalul bonului.</summary>
    private async Task<Result<LedgerEntry>> BankPaymentAsync(ConfirmExpenseDocumentCommand command, decimal total, CancellationToken cancellationToken)
    {
        if (command.LedgerEntryId is not { } id)
        {
            return Result.Failure<LedgerEntry>(ExpenseDocumentErrors.PaymentRequired);
        }

        LedgerEntry? payment = await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == id && e.PfaRegistrationId == command.PfaId, cancellationToken);
        if (payment is null || payment.BankTransactionId is null || payment.Amount >= 0)
        {
            return Result.Failure<LedgerEntry>(LedgerErrors.EntryNotFound);
        }

        if (payment.SourceDocumentId is not null || payment.EFacturaMessageId is not null)
        {
            return Result.Failure<LedgerEntry>(ExpenseDocumentErrors.PaymentTaken);
        }

        if (Math.Abs(payment.Amount) != total)
        {
            return Result.Failure<LedgerEntry>(ExpenseDocumentErrors.TotalMismatch(total, Math.Abs(payment.Amount)));
        }

        Result editable = await UpdateLedgerEntryCommandHandler.EnsureEditableAsync(db, payment, cancellationToken);
        return editable.IsFailure ? Result.Failure<LedgerEntry>(editable.Error) : payment;
    }
}
