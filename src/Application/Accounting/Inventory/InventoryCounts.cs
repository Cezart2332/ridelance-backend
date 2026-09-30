using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Accounting.Assets;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.Documents;
using Application.Accounting.Registers;
using Domain.Accounting;
using Domain.Taxes;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Inventory;

// Registrul-inventar (spec registre §5): o inventariere la o dată, precompletată din sistem, confirmată
// de PFA, revizuită și finalizată de Admin. Registrul e rezultatul inventarierii, nu al tranzacțiilor.

internal static class InventoryErrors
{
    public static readonly Error NotFound = Error.NotFound("Accounting.InventoryNotFound", "Inventarierea nu există.");

    public static readonly Error ItemNotFound = Error.NotFound("Accounting.InventoryItemNotFound", "Elementul nu există în inventariere.");

    public static readonly Error Exists = Error.Conflict("Accounting.InventoryExists", "Există deja o inventariere la această dată, cu același motiv.");

    public static readonly Error ReadOnly = Error.Conflict("Accounting.InventoryReadOnly", "Inventarierea e finală și nu se mai modifică.");

    public static readonly Error WrongStep = Error.Conflict("Accounting.InventoryWrongStep", "Inventarierea nu e la acest pas.");

    public static readonly Error InvalidValue = Error.Problem("Accounting.InventoryInvalid", "Descrierea e obligatorie, iar valoarea nu poate fi negativă.");

    public static readonly Error NoteRequired = Error.Problem("Accounting.InventoryNoteRequired", "O diferență, un element scos sau adăugat cere o notă.");

    public static Error Unconfirmed(IEnumerable<string> items) =>
        Error.Problem("Accounting.InventoryUnconfirmed", $"Mai sunt de confirmat: {string.Join(", ", items)}.");

    public static Error MissingNotes(IEnumerable<string> items) =>
        Error.Problem("Accounting.InventoryNoteRequired", $"Diferențele cer o notă: {string.Join(", ", items)}.");
}

/// <summary>Ce poate face PFA-ul sau Adminul cu un element.</summary>
public enum InventoryItemAction
{
    /// <summary>Valoarea din sistem e cea reală.</summary>
    Confirm = 0,

    /// <summary>Valoarea reală e alta (numerarul numărat, soldul din extras).</summary>
    Adjust = 1,

    /// <summary>Bunul nu mai există: „scos din folosință”.</summary>
    Remove = 2,

    /// <summary>Doar nota (revizuirea Adminului).</summary>
    Note = 3,
}

/// <summary>Precompletarea (spec registre §5): elementele pe care sistemul le cunoaște la o dată.</summary>
internal static class InventoryPrefill
{
    public static async Task<List<InventoryItem>> BuildAsync(IApplicationDbContext db, Guid pfaId, DateOnly date, CancellationToken cancellationToken)
    {
        List<InventoryItem> items = [];

        // Mijloacele fixe la valoarea rămasă, obiectele de inventar la valoarea de intrare. Mașina fără
        // decizia Adminului nu e activ, deci nu apare (scenariul 8).
        List<AssetDto> assets = await AssetSupport.DtosAsync(
            db,
            db.PfaAssets.Where(a => a.PfaRegistrationId == pfaId && a.EntryDate <= date && (a.DisposalDate == null || a.DisposalDate > date)),
            date,
            cancellationToken);
        items.AddRange(assets.Select(asset => Item(
            asset.Kind == AssetKind.FixedAsset ? InventoryCategory.FixedAssets : InventoryCategory.InventoryObjects,
            $"{asset.InventoryNumber} {asset.Name}",
            asset.Remaining,
            nameof(PfaAsset),
            asset.Id,
            requiresConfirmation: false)));

        items.AddRange(await BankAsync(db, pfaId, date, cancellationToken));

        // Numerarul calculat din ledger: încasări − plăți în numerar, de la început. Se confirmă obligatoriu.
        decimal cash = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfaId && e.Date <= date && e.PaymentMethod == PaymentMethod.Cash && !e.ClosedPeriodFlag &&
                        e.TransactionType != LedgerTransactionType.PlatformSettlement && e.ReconciliationStatus != ReconciliationStatus.NeedsReconciliation &&
                        !(e.BankTransactionId == null && e.PlatformDocumentId != null))
            .SumAsync(e => e.Amount, cancellationToken);
        items.Add(Item(InventoryCategory.Cash, "Numerar în casă (calculat din registru)", cash, "Ledger", null, requiresConfirmation: true));

        items.AddRange(await DebtsAsync(db, pfaId, date, cancellationToken));
        return items;
    }

    /// <summary>
    /// Disponibilul bancar pe fiecare cont conectat: soldul raportat de bancă minus mișcările de după
    /// dată; fără sold raportat, suma mișcărilor importate până la dată. Se confirmă cu extrasul.
    /// </summary>
    private static async Task<List<InventoryItem>> BankAsync(IApplicationDbContext db, Guid pfaId, DateOnly date, CancellationToken cancellationToken)
    {
        Guid userId = await db.PfaRegistrations.AsNoTracking().Where(p => p.Id == pfaId).Select(p => p.UserId).SingleAsync(cancellationToken);
        var accounts = await db.BankAccounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.IsActive)
            .Select(a => new { a.Id, a.Iban, a.Balance, a.BalanceDate, a.Connection.InstitutionId })
            .ToListAsync(cancellationToken);
        List<InventoryItem> items = [];
        foreach (var account in accounts)
        {
            decimal value;
            string description = $"Cont {account.Iban ?? account.InstitutionId}";
            if (account.Balance is { } balance && account.BalanceDate is { } balanceDate && balanceDate >= date)
            {
                decimal after = await db.BankTransactions.AsNoTracking()
                    .Where(t => t.BankAccountId == account.Id && !t.IsPending && (t.BookingDate ?? t.ValueDate) > date && (t.BookingDate ?? t.ValueDate) <= balanceDate)
                    .SumAsync(t => t.Amount, cancellationToken);
                value = balance - after;
            }
            else
            {
                value = await db.BankTransactions.AsNoTracking()
                    .Where(t => t.BankAccountId == account.Id && !t.IsPending && (t.BookingDate ?? t.ValueDate) <= date)
                    .SumAsync(t => t.Amount, cancellationToken);
                description += " (din mișcările importate)";
            }

            items.Add(Item(InventoryCategory.Bank, description, value, "BankAccount", account.Id, requiresConfirmation: true));
        }

        return items;
    }

    /// <summary>
    /// Datoriile: facturile primite neplătite (integral sau parțial) la dată, cu restul de plată, și
    /// obligațiile fiscale stabilite și neachitate. O factură neplătită nu e în RJIP (scenariul 7).
    /// </summary>
    private static async Task<List<InventoryItem>> DebtsAsync(IApplicationDbContext db, Guid pfaId, DateOnly date, CancellationToken cancellationToken)
    {
        var invoices = await db.EFacturaMessages.AsNoTracking()
            .Where(m => m.PfaRegistrationId == pfaId && m.Kind == EFacturaMessageKind.Received && !m.IsCreditNote &&
                        m.IssueDate != null && m.IssueDate <= date && m.TotalAmount != null && m.TotalAmount > 0)
            .Select(m => new
            {
                m.Id,
                m.InvoiceNumber,
                m.SupplierName,
                m.TotalAmount,
                Paid = db.LedgerEntries.Where(e => e.EFacturaMessageId == m.Id && e.Date <= date).Sum(e => -e.Amount),
            })
            .ToListAsync(cancellationToken);
        List<InventoryItem> items = [.. invoices
            .Where(i => i.TotalAmount!.Value - i.Paid > 0)
            .Select(i => Item(
                InventoryCategory.Debts,
                $"Factura {i.InvoiceNumber} – {i.SupplierName}",
                i.TotalAmount!.Value - i.Paid,
                nameof(EFacturaMessage),
                i.Id,
                requiresConfirmation: false))];

        var obligations = await db.TaxObligations.AsNoTracking()
            .Where(o => o.PfaRegistrationId == pfaId && o.Status != TaxObligationStatus.Platita && o.AmountDue > 0 &&
                        (o.PeriodYear < date.Year || o.PeriodYear == date.Year && o.PeriodMonth <= date.Month))
            .Select(o => new { o.Id, o.Type, o.PeriodYear, o.PeriodMonth, o.AmountDue })
            .ToListAsync(cancellationToken);
        items.AddRange(obligations.Select(o => Item(
            InventoryCategory.Debts,
            $"Obligație fiscală {o.Type} {o.PeriodMonth:00}.{o.PeriodYear}",
            o.AmountDue,
            nameof(TaxObligation),
            o.Id,
            requiresConfirmation: false)));
        return items;
    }

    private static InventoryItem Item(InventoryCategory category, string description, decimal value, string? sourceType, Guid? sourceId, bool requiresConfirmation) => new()
    {
        Id = Guid.NewGuid(),
        Category = category,
        Description = description.Length > 500 ? description[..500] : description,
        SystemValue = LedgerInvariants.Round(value),
        SourceType = sourceType,
        SourceId = sourceId,
        Status = InventoryItemStatus.Prefilled,
        RequiresConfirmation = requiresConfirmation,
    };
}

internal static class InventorySupport
{
    public static async Task<InventoryCount?> LoadAsync(IApplicationDbContext db, Guid pfaId, Guid countId, CancellationToken cancellationToken) =>
        await db.InventoryCounts.Include(c => c.Items).SingleOrDefaultAsync(c => c.Id == countId && c.PfaRegistrationId == pfaId, cancellationToken);

    public static async Task<InventoryCountDto> DtoAsync(IApplicationDbContext db, InventoryCount count, CancellationToken cancellationToken)
    {
        Dictionary<Guid, UserRef> users = await PlatformDocumentSupport.UsersAsync(db, [count.FinalizedByUserId], cancellationToken);
        List<InventoryItemDto> items = [.. count.Items
            .OrderBy(i => i.Category)
            .ThenBy(i => i.Description, StringComparer.Ordinal)
            .Select(i => new InventoryItemDto(
                i.Id, i.Category, i.Description, i.SystemValue, i.ConfirmedValue, i.Difference, i.SourceType, i.SourceId, i.Status, i.RequiresConfirmation, i.Note))];
        return new InventoryCountDto(
            count.Id,
            count.PfaRegistrationId,
            count.Date,
            count.Reason,
            count.Status,
            count.SubmittedAtUtc,
            count.FinalizedAtUtc,
            count.FinalizedByUserId is { } by ? users.GetValueOrDefault(by) : null,
            count.SnapshotDocumentId,
            items,
            Total(count.Items));
    }

    /// <summary>Valoarea de inventar a unui element: cea confirmată, altfel cea din sistem; cel scos nu contează.</summary>
    public static decimal Value(InventoryItem item) => item.Status == InventoryItemStatus.Removed ? 0 : item.ConfirmedValue ?? item.SystemValue;

    public static decimal Total(IEnumerable<InventoryItem> items) => items.Sum(Value);

    /// <summary>Un element are nevoie de notă: diferență, scos sau adăugat manual.</summary>
    public static bool NeedsNote(InventoryItem item) =>
        item.Difference != 0 || item.Status is InventoryItemStatus.Removed or InventoryItemStatus.AddedManually;

    public static string Label(InventoryCategory category) => category switch
    {
        InventoryCategory.FixedAssets => "Mijloace fixe",
        InventoryCategory.InventoryObjects => "Obiecte de inventar",
        InventoryCategory.Stocks => "Stocuri",
        InventoryCategory.Receivables => "Creanțe",
        InventoryCategory.Bank => "Disponibil în bancă",
        InventoryCategory.Cash => "Numerar",
        _ => "Datorii",
    };

    /// <summary>Registrul-inventar (OMFP 170/2015, cod 14-1-2/b) din elementele inventarierii, grupat pe categorii.</summary>
    public static RegisterDocument Document(string name, string cui, InventoryCount count, bool preview)
    {
        List<RegisterLine> lines = [];
        int number = 0;
        foreach (IGrouping<InventoryCategory, InventoryItem> group in count.Items
                     .Where(i => i.Status != InventoryItemStatus.Removed)
                     .OrderBy(i => i.Category)
                     .ThenBy(i => i.Description, StringComparer.Ordinal)
                     .GroupBy(i => i.Category))
        {
            lines.AddRange(group.Select(item => new RegisterLine([++number, item.Description, Value(item)])));
            lines.Add(new RegisterLine([null, $"Total {Label(group.Key)}", group.Sum(Value)], Emphasis: true));
        }

        string reason = count.Reason switch
        {
            InventoryReason.ActivityStart => "începerea activității",
            InventoryReason.Cessation => "încetarea activității",
            _ => "sfârșitul anului",
        };
        List<string> notes = ["Model conform OMFP nr. 170/2015. Datoriile sunt valorile rămase de plată la data inventarului."];
        if (preview)
        {
            notes.Insert(0, "Situație precompletată din sistem, neconfirmată prin inventariere.");
        }

        return new RegisterDocument(
            "REGISTRUL-INVENTAR",
            "14-1-2/b",
            [$"{name} — CUI {cui}", $"la data de {RegisterData.Date(count.Date)} ({reason})"],
            [
                new("Nr. crt.", Width: 0.5f),
                new("Denumirea elementelor inventariate", Width: 4f),
                new("Valoarea de inventar", Numeric: true, Width: 1.2f),
            ],
            ["1", "2", "3"],
            lines,
            notes);
    }
}

// ─── Admin ───────────────────────────────────────────────────────────────────────────────────────

/// <summary><c>GET /accounting/pfas/{pfaId}/inventory-counts</c></summary>
public sealed record ListInventoryCountsQuery(Guid PfaId) : IQuery<IReadOnlyList<InventoryCountDto>>;

internal sealed class ListInventoryCountsQueryHandler(IApplicationDbContext db) : IQueryHandler<ListInventoryCountsQuery, IReadOnlyList<InventoryCountDto>>
{
    public async Task<Result<IReadOnlyList<InventoryCountDto>>> Handle(ListInventoryCountsQuery query, CancellationToken cancellationToken)
    {
        List<InventoryCount> counts = await db.InventoryCounts.AsNoTracking()
            .Include(c => c.Items)
            .Where(c => c.PfaRegistrationId == query.PfaId)
            .OrderByDescending(c => c.Date)
            .ToListAsync(cancellationToken);
        List<InventoryCountDto> dtos = [];
        foreach (InventoryCount count in counts)
        {
            dtos.Add(await InventorySupport.DtoAsync(db, count, cancellationToken));
        }

        return dtos;
    }
}

/// <summary>
/// <c>POST /accounting/pfas/{pfaId}/inventory-counts</c> — <c>{ date, reason }</c>: inventarierea
/// precompletată, trimisă PFA-ului spre confirmare (§5 pașii 1–2). Jobul de la 31.12 o face singur.
/// </summary>
public sealed record StartInventoryCountCommand(Guid PfaId, DateOnly Date, InventoryReason Reason) : ICommand<InventoryCountDto>;

internal sealed class StartInventoryCountCommandHandler(IApplicationDbContext db, IUserContext? userContext = null)
    : ICommandHandler<StartInventoryCountCommand, InventoryCountDto>
{
    public async Task<Result<InventoryCountDto>> Handle(StartInventoryCountCommand command, CancellationToken cancellationToken)
    {
        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == command.PfaId, cancellationToken))
        {
            return Result.Failure<InventoryCountDto>(AccountingErrors.PfaNotFound);
        }

        if (await db.InventoryCounts.AnyAsync(c => c.PfaRegistrationId == command.PfaId && c.Date == command.Date && c.Reason == command.Reason, cancellationToken))
        {
            return Result.Failure<InventoryCountDto>(InventoryErrors.Exists);
        }

        var count = new InventoryCount
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = command.PfaId,
            Date = command.Date,
            Reason = command.Reason,
            Status = InventoryStatus.AwaitingPfaConfirmation,
            CreatedAtUtc = DateTime.UtcNow,
            Items = await InventoryPrefill.BuildAsync(db, command.PfaId, command.Date, cancellationToken),
        };
        count.Items.ForEach(i => i.InventoryCountId = count.Id);
        db.InventoryCounts.Add(count);
        AccountingAudit.Record(db, command.PfaId, nameof(InventoryCount), count.Id, "CREATE", null,
            new { count.Date, count.Reason, items = count.Items.Count }, null, userContext?.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await InventorySupport.DtoAsync(db, count, cancellationToken);
    }
}

/// <summary>
/// <c>PATCH …/inventory-counts/{id}/items/{itemId}</c>: confirmă, corectează, scoate sau notează. PFA-ul
/// lucrează la confirmare; Adminul și la revizuire. O inventariere finală nu se mai modifică.
/// </summary>
public sealed record UpdateInventoryItemCommand(Guid PfaId, Guid CountId, Guid ItemId, InventoryItemAction Action, decimal? Value, string? Note, bool ByAdmin)
    : ICommand<InventoryCountDto>;

internal sealed class UpdateInventoryItemCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<UpdateInventoryItemCommand, InventoryCountDto>
{
    public async Task<Result<InventoryCountDto>> Handle(UpdateInventoryItemCommand command, CancellationToken cancellationToken)
    {
        if (await InventorySupport.LoadAsync(db, command.PfaId, command.CountId, cancellationToken) is not { } count)
        {
            return Result.Failure<InventoryCountDto>(InventoryErrors.NotFound);
        }

        Result step = Editable(count, command.ByAdmin);
        if (step.IsFailure)
        {
            return Result.Failure<InventoryCountDto>(step.Error);
        }

        if (count.Items.SingleOrDefault(i => i.Id == command.ItemId) is not { } item)
        {
            return Result.Failure<InventoryCountDto>(InventoryErrors.ItemNotFound);
        }

        var before = new { item.Status, item.ConfirmedValue, item.Note };
        string? note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        switch (command.Action)
        {
            case InventoryItemAction.Confirm:
                item.ConfirmedValue = item.SystemValue;
                item.Status = item.Status == InventoryItemStatus.AddedManually ? item.Status : InventoryItemStatus.Confirmed;
                break;
            case InventoryItemAction.Adjust when command.Value is { } value && value >= 0:
                item.ConfirmedValue = LedgerInvariants.Round(value);
                if (item.Status != InventoryItemStatus.AddedManually)
                {
                    item.Status = item.ConfirmedValue == item.SystemValue ? InventoryItemStatus.Confirmed : InventoryItemStatus.Adjusted;
                }

                break;
            case InventoryItemAction.Remove when note is not null:
                item.Status = InventoryItemStatus.Removed;
                break;
            case InventoryItemAction.Remove:
                return Result.Failure<InventoryCountDto>(InventoryErrors.NoteRequired);
            case InventoryItemAction.Note:
                break;
            default:
                return Result.Failure<InventoryCountDto>(InventoryErrors.InvalidValue);
        }

        item.Note = note ?? item.Note;
        AccountingAudit.Record(db, command.PfaId, nameof(InventoryItem), item.Id, command.Action.ToString().ToUpperInvariant(), before,
            new { item.Status, item.ConfirmedValue, item.Note }, note, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await InventorySupport.DtoAsync(db, count, cancellationToken);
    }

    internal static Result Editable(InventoryCount count, bool byAdmin)
    {
        if (count.Status == InventoryStatus.Final)
        {
            return Result.Failure(InventoryErrors.ReadOnly);
        }

        return byAdmin || count.Status is InventoryStatus.Draft or InventoryStatus.AwaitingPfaConfirmation
            ? Result.Success()
            : Result.Failure(InventoryErrors.WrongStep);
    }
}

/// <summary><c>POST …/inventory-counts/{id}/items</c> — un element pe care sistemul nu îl știe.</summary>
public sealed record AddInventoryItemCommand(Guid PfaId, Guid CountId, InventoryCategory Category, string Description, decimal Value, string? Note, bool ByAdmin)
    : ICommand<InventoryCountDto>;

internal sealed class AddInventoryItemCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<AddInventoryItemCommand, InventoryCountDto>
{
    public async Task<Result<InventoryCountDto>> Handle(AddInventoryItemCommand command, CancellationToken cancellationToken)
    {
        if (await InventorySupport.LoadAsync(db, command.PfaId, command.CountId, cancellationToken) is not { } count)
        {
            return Result.Failure<InventoryCountDto>(InventoryErrors.NotFound);
        }

        Result step = UpdateInventoryItemCommandHandler.Editable(count, command.ByAdmin);
        if (step.IsFailure)
        {
            return Result.Failure<InventoryCountDto>(step.Error);
        }

        if (string.IsNullOrWhiteSpace(command.Description) || command.Value < 0 || !Enum.IsDefined(command.Category))
        {
            return Result.Failure<InventoryCountDto>(InventoryErrors.InvalidValue);
        }

        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            InventoryCountId = count.Id,
            Category = command.Category,
            Description = command.Description.Trim(),
            SystemValue = 0,
            ConfirmedValue = LedgerInvariants.Round(command.Value),
            Status = InventoryItemStatus.AddedManually,
            Note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim(),
        };
        // Adăugat explicit (are cheia setată); EF îl pune și în colecția inventarierii urmărite.
        db.InventoryItems.Add(item);
        AccountingAudit.Record(db, command.PfaId, nameof(InventoryItem), item.Id, "ADD", null,
            new { item.Category, item.Description, item.ConfirmedValue }, item.Note, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await InventorySupport.DtoAsync(db, count, cancellationToken);
    }
}

/// <summary>
/// <c>POST …/inventory-counts/{id}/submit</c> — PFA-ul a terminat: fiecare element e confirmat,
/// corectat sau scos, iar numerarul și banca sunt confirmate obligatoriu. Trece la revizuirea Adminului.
/// </summary>
public sealed record SubmitInventoryCountCommand(Guid PfaId, Guid CountId) : ICommand<InventoryCountDto>;

internal sealed class SubmitInventoryCountCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<SubmitInventoryCountCommand, InventoryCountDto>
{
    public async Task<Result<InventoryCountDto>> Handle(SubmitInventoryCountCommand command, CancellationToken cancellationToken)
    {
        if (await InventorySupport.LoadAsync(db, command.PfaId, command.CountId, cancellationToken) is not { } count)
        {
            return Result.Failure<InventoryCountDto>(InventoryErrors.NotFound);
        }

        if (count.Status is not (InventoryStatus.Draft or InventoryStatus.AwaitingPfaConfirmation))
        {
            return Result.Failure<InventoryCountDto>(count.Status == InventoryStatus.Final ? InventoryErrors.ReadOnly : InventoryErrors.WrongStep);
        }

        List<string> open = [.. count.Items.Where(i => i.Status == InventoryItemStatus.Prefilled).Select(i => i.Description)];
        if (open.Count > 0)
        {
            return Result.Failure<InventoryCountDto>(InventoryErrors.Unconfirmed(open));
        }

        count.Status = InventoryStatus.AwaitingAdminReview;
        count.SubmittedAtUtc = DateTime.UtcNow;
        AccountingAudit.Record(db, command.PfaId, nameof(InventoryCount), count.Id, "SUBMIT", null, new { count.Status }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await InventorySupport.DtoAsync(db, count, cancellationToken);
    }
}

/// <summary>
/// <c>POST …/inventory-counts/{id}/finalize</c> (§5 pașii 3–4): orice diferență, element scos sau
/// adăugat are notă; statusul devine <c>FINAL</c>, se salvează PDF-ul registrului și totul devine read-only.
/// </summary>
public sealed record FinalizeInventoryCountCommand(Guid PfaId, Guid CountId) : ICommand<InventoryCountDto>;

internal sealed class FinalizeInventoryCountCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    IRegisterExporter? exporter = null,
    DeclarationFiles? files = null)
    : ICommandHandler<FinalizeInventoryCountCommand, InventoryCountDto>
{
    public async Task<Result<InventoryCountDto>> Handle(FinalizeInventoryCountCommand command, CancellationToken cancellationToken)
    {
        if (await InventorySupport.LoadAsync(db, command.PfaId, command.CountId, cancellationToken) is not { } count)
        {
            return Result.Failure<InventoryCountDto>(InventoryErrors.NotFound);
        }

        if (count.Status != InventoryStatus.AwaitingAdminReview)
        {
            return Result.Failure<InventoryCountDto>(count.Status == InventoryStatus.Final ? InventoryErrors.ReadOnly : InventoryErrors.WrongStep);
        }

        List<string> missing = [.. count.Items.Where(i => InventorySupport.NeedsNote(i) && string.IsNullOrWhiteSpace(i.Note)).Select(i => i.Description)];
        if (missing.Count > 0)
        {
            return Result.Failure<InventoryCountDto>(InventoryErrors.MissingNotes(missing));
        }

        count.Status = InventoryStatus.Final;
        count.FinalizedAtUtc = DateTime.UtcNow;
        count.FinalizedByUserId = userContext.UserId;
        if (exporter is not null && files is not null && await RegisterData.PfaAsync(db, command.PfaId, cancellationToken) is { } pfa)
        {
            byte[] pdf = exporter.ToPdf(InventorySupport.Document(pfa.Name, pfa.Cui, count, preview: false));
            count.SnapshotDocumentId = (await files.StoreAsync(command.PfaId, pdf, $"Registru-inventar_{pfa.Cui}_{count.Date:yyyyMMdd}.pdf", "application/pdf", cancellationToken)).Id;
        }

        AccountingAudit.Record(db, command.PfaId, nameof(InventoryCount), count.Id, "FINALIZE", null,
            new { count.Status, total = InventorySupport.Total(count.Items) }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await InventorySupport.DtoAsync(db, count, cancellationToken);
    }
}

// ─── Registrul-inventar ──────────────────────────────────────────────────────────────────────────

/// <summary><c>GET …/registers/inventory/export?year&amp;format</c></summary>
public sealed record ExportInventoryQuery(Guid PfaId, int Year, RegisterFormat Format) : IQuery<RegisterFile>;

/// <summary>
/// Registrul-inventar al anului (OMFP 170/2015, cod 14-1-2/b): inventarierea de la 31.12 sau de la
/// încetare, cea finală dacă există. Fără inventariere, precompletarea sistemului, marcată ca neconfirmată.
/// </summary>
internal sealed class ExportInventoryQueryHandler(IApplicationDbContext db, IRegisterExporter exporter) : IQueryHandler<ExportInventoryQuery, RegisterFile>
{
    public async Task<Result<RegisterFile>> Handle(ExportInventoryQuery query, CancellationToken cancellationToken)
    {
        if (query.Year is < 2000 or > 2100)
        {
            return Result.Failure<RegisterFile>(RegisterErrors.InvalidYear);
        }

        if (await RegisterData.PfaAsync(db, query.PfaId, cancellationToken) is not { } pfa)
        {
            return Result.Failure<RegisterFile>(AccountingErrors.PfaNotFound);
        }

        InventoryCount? count = await db.InventoryCounts.AsNoTracking()
            .Include(c => c.Items)
            .Where(c => c.PfaRegistrationId == query.PfaId && c.Date.Year == query.Year && c.Reason != InventoryReason.ActivityStart)
            .OrderByDescending(c => c.Status == InventoryStatus.Final)
            .ThenByDescending(c => c.Date)
            .FirstOrDefaultAsync(cancellationToken);
        bool preview = count is not { Status: InventoryStatus.Final };
        if (count is null)
        {
            DateOnly? ended = await db.PfaAccountingEngagements.AsNoTracking()
                .Where(e => e.PfaRegistrationId == query.PfaId && e.Status == EngagementStatus.Inactive && e.EndDate != null)
                .OrderByDescending(e => e.EndDate)
                .Select(e => e.EndDate)
                .FirstOrDefaultAsync(cancellationToken);
            DateOnly date = ended is { } end && end.Year == query.Year ? end : new DateOnly(query.Year, 12, 31);
            count = new InventoryCount
            {
                PfaRegistrationId = query.PfaId,
                Date = date,
                Reason = date.Month == 12 && date.Day == 31 ? InventoryReason.YearEnd : InventoryReason.Cessation,
                Items = await InventoryPrefill.BuildAsync(db, query.PfaId, date, cancellationToken),
            };
        }

        RegisterDocument document = InventorySupport.Document(pfa.Name, pfa.Cui, count, preview);
        return RegisterFiles.Export(exporter, document, query.Format, $"Registru-inventar_{pfa.Cui}_{count.Date:yyyyMMdd}");
    }
}
