using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Ledger;

internal static class LedgerErrors
{
    public static readonly Error EntryNotFound = Error.NotFound("Accounting.LedgerEntryNotFound", "Înregistrarea nu există.");

    public static readonly Error Locked = Error.Conflict("Accounting.LedgerEntryLocked", "Înregistrarea e blocată; se corectează doar prin corecție controlată.");

    public static readonly Error CategoryRequired = Error.Conflict("Accounting.CategoryRequired", "Alege categoria cheltuielii înainte de verificare.");

    public static readonly Error DescriptionRequired = Error.Problem("Accounting.DescriptionRequired", "Descrierea e obligatorie.");

    public static readonly Error AmountRequired = Error.Problem("Accounting.AmountRequired", "Suma trebuie să fie diferită de zero.");
}

/// <summary><c>GET /accounting/pfas/{pfaId}/ledger?from&amp;to&amp;status&amp;type&amp;source&amp;page&amp;pageSize</c></summary>
public sealed record ListLedgerQuery(
    Guid PfaId,
    DateOnly? From,
    DateOnly? To,
    LedgerEntryStatus? Status,
    LedgerTransactionType? Type,
    LedgerSource? Source,
    int Page = 1,
    int PageSize = 25) : IQuery<Paged<LedgerEntryDto>>;

internal sealed class ListLedgerQueryHandler(IApplicationDbContext db) : IQueryHandler<ListLedgerQuery, Paged<LedgerEntryDto>>
{
    public async Task<Result<Paged<LedgerEntryDto>>> Handle(ListLedgerQuery query, CancellationToken cancellationToken)
    {
        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == query.PfaId, cancellationToken))
        {
            return Result.Failure<Paged<LedgerEntryDto>>(AccountingErrors.PfaNotFound);
        }

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        IQueryable<LedgerEntry> entries = db.LedgerEntries.AsNoTracking().Where(e => e.PfaRegistrationId == query.PfaId);
        if (query.From is { } from)
        {
            entries = entries.Where(e => e.Date >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(e => e.Date <= to);
        }

        if (query.Status is { } status)
        {
            entries = entries.Where(e => e.Status == status);
        }

        if (query.Type is { } type)
        {
            entries = entries.Where(e => e.TransactionType == type);
        }

        if (query.Source is { } source)
        {
            entries = entries.Where(e => e.Source == source);
        }

        int total = await entries.CountAsync(cancellationToken);
        List<LedgerEntryDto> items = await LedgerSupport.DtosAsync(
            entries.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize),
            cancellationToken);
        return new Paged<LedgerEntryDto>(items, page, pageSize, total);
    }
}

/// <summary><c>PATCH /accounting/ledger/{id}</c> — câmpurile schimbate și motivul (obligatoriu).</summary>
public sealed record UpdateLedgerEntryCommand(Guid Id, JsonElement Fields, string? Reason) : ICommand<LedgerEntryDto>;

/// <summary>
/// Modificarea manuală a unei înregistrări, cu motiv și audit. Deductibilitatea se recalculează la
/// noua dată și categorie. <c>sourceDocumentId</c> confirmă potrivirea propusă la încărcarea unui
/// document de cheltuială. Luna veche și cea nouă trebuie să fie deschise.
/// </summary>
internal sealed class UpdateLedgerEntryCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<UpdateLedgerEntryCommand, LedgerEntryDto>
{
    private static readonly string[] Editable =
        ["date", "documentLabel", "counterparty", "description", "transactionType", "paymentMethod", "amount", "category", "sourceDocumentId"];

    public async Task<Result<LedgerEntryDto>> Handle(UpdateLedgerEntryCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<LedgerEntryDto>(AccountingErrors.ReasonRequired);
        }

        LedgerEntry? entry = await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == command.Id, cancellationToken);
        if (entry is null)
        {
            return Result.Failure<LedgerEntryDto>(LedgerErrors.EntryNotFound);
        }

        Result open = await EnsureEditableAsync(db, entry, cancellationToken);
        if (open.IsFailure)
        {
            return Result.Failure<LedgerEntryDto>(open.Error);
        }

        if (command.Fields.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<LedgerEntryDto>(AccountingErrors.NoChanges);
        }

        var before = new Dictionary<string, object?>();
        var after = new Dictionary<string, object?>();
        foreach (JsonProperty field in command.Fields.EnumerateObject().Where(f => Editable.Contains(f.Name)))
        {
            Result applied = await ApplyAsync(entry, field, before, after, cancellationToken);
            if (applied.IsFailure)
            {
                return Result.Failure<LedgerEntryDto>(applied.Error);
            }
        }

        if (after.Count == 0)
        {
            return Result.Failure<LedgerEntryDto>(AccountingErrors.NoChanges);
        }

        string period = LedgerSupport.PeriodOf(entry.Date);
        if (period != entry.AccountingPeriod)
        {
            Result target = await PlatformDocumentSupport.EnsureWritableAsync(db, entry.PfaRegistrationId, period, cancellationToken);
            if (target.IsFailure)
            {
                return Result.Failure<LedgerEntryDto>(target.Error);
            }

            entry.AccountingPeriod = period;
        }

        DeductibilityService.Resolve(entry, await LedgerSupport.RulesAsync(db, entry.PfaRegistrationId, cancellationToken));
        AccountingAudit.Record(db, entry.PfaRegistrationId, nameof(LedgerEntry), entry.Id, "UPDATE", before, after, command.Reason.Trim(), userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await LedgerSupport.DtoAsync(db, entry.Id, cancellationToken);
    }

    public static async Task<Result> EnsureEditableAsync(IApplicationDbContext db, LedgerEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Status == LedgerEntryStatus.Locked)
        {
            return Result.Failure(LedgerErrors.Locked);
        }

        return await PlatformDocumentSupport.EnsureWritableAsync(db, entry.PfaRegistrationId, entry.AccountingPeriod, cancellationToken);
    }

    private async Task<Result> ApplyAsync(
        LedgerEntry entry,
        JsonProperty field,
        Dictionary<string, object?> before,
        Dictionary<string, object?> after,
        CancellationToken cancellationToken)
    {
        JsonElement value = field.Value;
        bool empty = value.ValueKind == JsonValueKind.Null;
        try
        {
            switch (field.Name)
            {
                case "date":
                    Set(field.Name, entry.Date, value.Deserialize<DateOnly>(), v => entry.Date = v);
                    break;
                case "documentLabel" when !empty && value.GetString() is { Length: > 0 } label:
                    Set(field.Name, entry.DocumentLabel, LedgerSupport.Cut(label.Trim(), LedgerSupport.DocumentLabelLength), v => entry.DocumentLabel = v);
                    break;
                case "counterparty":
                    Set(field.Name, entry.Counterparty, empty ? null : LedgerSupport.Cut(value.GetString()!.Trim(), LedgerSupport.CounterpartyLength), v => entry.Counterparty = v);
                    break;
                case "description" when !empty && value.GetString() is { Length: > 0 } description:
                    Set(field.Name, entry.Description, LedgerSupport.Cut(description.Trim(), LedgerSupport.DescriptionLength), v => entry.Description = v);
                    break;
                case "transactionType":
                    Set(field.Name, entry.TransactionType, value.Deserialize<LedgerTransactionType>(AccountingJson.Options), v => entry.TransactionType = v);
                    break;
                case "paymentMethod":
                    Set(field.Name, entry.PaymentMethod, value.Deserialize<PaymentMethod>(AccountingJson.Options), v => entry.PaymentMethod = v);
                    break;
                case "amount" when value.GetDecimal() != 0:
                    Set(field.Name, entry.Amount, value.GetDecimal(), v => entry.Amount = v);
                    break;
                case "category":
                    string? category = empty ? null : value.GetString();
                    if (category is not null && !await db.ExpenseCategoryRules.AnyAsync(r => r.Category == category, cancellationToken))
                    {
                        return Result.Failure(AccountingErrors.InvalidField("category"));
                    }

                    Set(field.Name, entry.Category, category, v => entry.Category = v);
                    break;
                case "sourceDocumentId":
                    Guid? documentId = empty ? null : value.GetGuid();
                    if (documentId is { } id && !await db.Documents.AnyAsync(d => d.Id == id && d.PfaRegistrationId == entry.PfaRegistrationId, cancellationToken))
                    {
                        return Result.Failure(AccountingErrors.InvalidField("sourceDocumentId"));
                    }

                    Set(field.Name, entry.SourceDocumentId, documentId, v => entry.SourceDocumentId = v);
                    await LinkExpenseDocumentAsync(entry, documentId, cancellationToken);
                    break;
                default:
                    return Result.Failure(AccountingErrors.InvalidField(field.Name));
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return Result.Failure(AccountingErrors.InvalidField(field.Name));
        }

        return Result.Success();

        void Set<T>(string name, T current, T next, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, next))
            {
                return;
            }

            before[name] = current;
            after[name] = next;
            assign(next);
        }
    }

    /// <summary>Potrivirea confirmată: documentul de cheltuială știe plata pe care o justifică.</summary>
    private async Task LinkExpenseDocumentAsync(LedgerEntry entry, Guid? documentId, CancellationToken cancellationToken)
    {
        if (documentId is not { } id ||
            await db.ExpenseDocuments.FirstOrDefaultAsync(d => d.DocumentId == id && d.PfaRegistrationId == entry.PfaRegistrationId, cancellationToken) is not { } expense)
        {
            return;
        }

        expense.LedgerEntryId = entry.Id;
        if (entry.Category is null && entry.TransactionType == LedgerTransactionType.Expense)
        {
            entry.Category = DeductibilityService.Classify(
                await db.ExpenseCategoryRules.AsNoTracking().ToListAsync(cancellationToken), entry.Date, expense.Merchant)?.Category;
        }
    }
}

/// <summary><c>POST /accounting/ledger/{id}/verify</c></summary>
public sealed record VerifyLedgerEntryCommand(Guid Id) : ICommand<LedgerEntryDto>;

internal sealed class VerifyLedgerEntryCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<VerifyLedgerEntryCommand, LedgerEntryDto>
{
    public async Task<Result<LedgerEntryDto>> Handle(VerifyLedgerEntryCommand command, CancellationToken cancellationToken)
    {
        LedgerEntry? entry = await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == command.Id, cancellationToken);
        if (entry is null)
        {
            return Result.Failure<LedgerEntryDto>(LedgerErrors.EntryNotFound);
        }

        Result open = await UpdateLedgerEntryCommandHandler.EnsureEditableAsync(db, entry, cancellationToken);
        if (open.IsFailure)
        {
            return Result.Failure<LedgerEntryDto>(open.Error);
        }

        if (entry.TransactionType == LedgerTransactionType.Expense && entry.Category is null)
        {
            return Result.Failure<LedgerEntryDto>(LedgerErrors.CategoryRequired);
        }

        LedgerEntryStatus from = entry.Status;
        entry.Status = LedgerEntryStatus.Verified;
        AccountingAudit.Record(db, entry.PfaRegistrationId, nameof(LedgerEntry), entry.Id, "VERIFY", new { status = from }, new { status = entry.Status }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await LedgerSupport.DtoAsync(db, entry.Id, cancellationToken);
    }
}

/// <summary><c>POST /accounting/pfas/{pfaId}/ledger/manual</c> — notă contabilă, sursă <c>MANUAL</c>.</summary>
public sealed record CreateManualLedgerEntryCommand(Guid PfaId, ManualLedgerEntryRequest Request) : ICommand<LedgerEntryDto>;

internal sealed class CreateManualLedgerEntryCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<CreateManualLedgerEntryCommand, LedgerEntryDto>
{
    public async Task<Result<LedgerEntryDto>> Handle(CreateManualLedgerEntryCommand command, CancellationToken cancellationToken)
    {
        ManualLedgerEntryRequest request = command.Request;
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Failure<LedgerEntryDto>(AccountingErrors.ReasonRequired);
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Result.Failure<LedgerEntryDto>(LedgerErrors.DescriptionRequired);
        }

        if (request.Amount == 0)
        {
            return Result.Failure<LedgerEntryDto>(LedgerErrors.AmountRequired);
        }

        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == command.PfaId, cancellationToken))
        {
            return Result.Failure<LedgerEntryDto>(AccountingErrors.PfaNotFound);
        }

        string period = LedgerSupport.PeriodOf(request.Date);
        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, command.PfaId, period, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<LedgerEntryDto>(writable.Error);
        }

        if (request.Category is not null && !await db.ExpenseCategoryRules.AnyAsync(r => r.Category == request.Category, cancellationToken))
        {
            return Result.Failure<LedgerEntryDto>(AccountingErrors.InvalidField("category"));
        }

        // Semnul urmează tipul, ca în formularul din frontend: plățile negative, încasările pozitive.
        decimal amount = request.TransactionType switch
        {
            LedgerTransactionType.Expense => -Math.Abs(request.Amount),
            LedgerTransactionType.Income => Math.Abs(request.Amount),
            _ => request.Amount,
        };
        LedgerEntry entry = LedgerSupport.New(
            command.PfaId,
            request.Date,
            LedgerSource.Manual,
            null,
            string.IsNullOrWhiteSpace(request.DocumentLabel) ? "Notă contabilă" : request.DocumentLabel.Trim(),
            request.Counterparty,
            request.Description.Trim(),
            request.TransactionType,
            request.PaymentMethod,
            amount,
            "RON",
            LedgerEntryStatus.Verified,
            new HashSet<string>());
        entry.Category = request.Category;
        entry.CreatedByUserId = userContext.UserId;
        DeductibilityService.Resolve(entry, await LedgerSupport.RulesAsync(db, command.PfaId, cancellationToken));
        db.LedgerEntries.Add(entry);
        AccountingAudit.Record(
            db, command.PfaId, nameof(LedgerEntry), entry.Id, "CREATE_MANUAL", null,
            new { entry.Date, entry.Description, entry.TransactionType, entry.PaymentMethod, entry.Amount, entry.Category },
            request.Reason.Trim(), userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await LedgerSupport.DtoAsync(db, entry.Id, cancellationToken);
    }
}
