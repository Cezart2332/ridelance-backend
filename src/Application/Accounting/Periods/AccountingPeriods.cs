using System.Globalization;
using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Application.Accounting.Ledger;
using Application.Accounting.Pfas;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Periods;

internal static class PeriodErrors
{
    public static readonly Error NotEnded = Error.Conflict("Accounting.PeriodNotEnded", "O lună se închide doar după ce s-a terminat.");

    public static readonly Error OutsideEngagement = Error.Problem("Accounting.PeriodOutsideEngagement", "Luna nu e în perioada colaborării contabile.");

    public static readonly Error CorrectionReasonRequired = Error.Problem("Accounting.ReasonRequired", "Motivul corecției e obligatoriu.");

    public static readonly Error EntryOutsidePeriod = Error.Problem("Accounting.EntryOutsidePeriod", "Tranzacția nu aparține perioadei corectate.");

    public static readonly Error CorrectionMovesPeriod = Error.Problem("Accounting.EntryOutsidePeriod", "O corecție nu poate muta tranzacția în altă perioadă.");

    public static Error AlreadyClosed(string period) => Error.Conflict("Accounting.PeriodClosed", $"Perioada {period} e deja închisă.");

    public static Error StillOpen(string period) => Error.Problem("Accounting.PeriodOpen", $"Perioada {period} e deschisă; modifică direct tranzacția.");

    public static Error NotReconciled(IEnumerable<string> failed) =>
        Error.Conflict("Accounting.MonthNotReconciled", $"Luna nu se poate închide: {string.Join(" ", failed)}");

    public static readonly Error ReopenReasonRequired = Error.Problem("Accounting.ReasonRequired", "Motivul redeschiderii e obligatoriu.");

    public static Error NotClosed(string period) => Error.Conflict("Accounting.PeriodOpen", $"Perioada {period} nu e închisă.");

    public static Error YearClosed(int year) => Error.Conflict("Accounting.YearClosed", $"Anul {year} e închis; se redeschide întâi anul.");

    public static readonly Error AlreadyStorned = Error.Conflict(
        "Accounting.AlreadyStorned", "Înregistrarea e deja stornată; corectează înregistrarea care o înlocuiește, din luna curentă.");

    public static readonly Error BankFacts = Error.Problem(
        "Accounting.LedgerInvariant", "Suma și canalul unei plăți bancare sunt cele din extras; corecția poate schimba doar celelalte câmpuri.");
}

/// <summary><c>GET /accounting/pfas/{pfaId}/periods</c> — lunile colaborării, cele mai noi întâi.</summary>
public sealed record ListPeriodsQuery(Guid PfaId) : IQuery<IReadOnlyList<AccountingPeriodDto>>;

/// <summary>
/// Lunile contabile ale unui PFA (§3.5): toate lunile de la începutul colaborării până la luna
/// curentă (sau până la încetare); o lună fără rând e <c>OPEN</c>.
/// </summary>
internal sealed class ListPeriodsQueryHandler(IApplicationDbContext db) : IQueryHandler<ListPeriodsQuery, IReadOnlyList<AccountingPeriodDto>>
{
    public async Task<Result<IReadOnlyList<AccountingPeriodDto>>> Handle(ListPeriodsQuery query, CancellationToken cancellationToken)
    {
        EngagementInfo? engagement = await PfaEngagements.InfoAsync(db, query.PfaId, cancellationToken);
        if (engagement is null)
        {
            return Result.Failure<IReadOnlyList<AccountingPeriodDto>>(AccountingErrors.PfaNotFound);
        }

        List<PfaAccountingPeriod> stored = await db.PfaAccountingPeriods.AsNoTracking()
            .Where(p => p.PfaRegistrationId == query.PfaId)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, UserRef> users = await PlatformDocumentSupport.UsersAsync(db, stored.Select(p => p.ClosedByUserId), cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly last = engagement.EndDate is { } end && end < today ? end : today;
        var periods = new SortedSet<string>(stored.Select(p => p.Period), StringComparer.Ordinal);
        for (var month = new DateOnly(engagement.StartDate.Year, engagement.StartDate.Month, 1); month <= last; month = month.AddMonths(1))
        {
            periods.Add(LedgerSupport.PeriodOf(month));
        }

        return periods.Reverse().Select(period =>
        {
            PfaAccountingPeriod? row = stored.FirstOrDefault(p => p.Period == period);
            return new AccountingPeriodDto(
                query.PfaId,
                period,
                row?.Status ?? AccountingPeriodStatus.Open,
                row?.ClosedByUserId is { } by && users.TryGetValue(by, out UserRef? user) ? user : null,
                row?.ClosedAtUtc);
        }).ToList();
    }
}

/// <summary><c>POST /accounting/pfas/{pfaId}/periods/{period}/close</c></summary>
public sealed record ClosePeriodCommand(Guid PfaId, string Period) : ICommand<AccountingPeriodDto>;

/// <summary>
/// Închiderea lunii (spec contabilitate B8): <c>CLOSED</c>, înregistrările ei trec în
/// <c>LOCKED</c>, iar de aici orice scriere (importatori, utilizatori) e refuzată; o corecție se face
/// doar prin <see cref="CreatePeriodCorrectionCommand"/>.
/// </summary>
internal sealed class ClosePeriodCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    Microsoft.Extensions.Options.IOptions<AccountingOptions>? options = null,
    IQueryHandler<Registers.ExportRjipQuery, Registers.RegisterFile>? rjipExport = null,
    Declarations.DeclarationFiles? files = null)
    : ICommandHandler<ClosePeriodCommand, AccountingPeriodDto>
{
    public async Task<Result<AccountingPeriodDto>> Handle(ClosePeriodCommand command, CancellationToken cancellationToken)
    {
        if (!PlatformDocumentSupport.IsValidPeriod(command.Period))
        {
            return Result.Failure<AccountingPeriodDto>(AccountingErrors.InvalidPeriod);
        }

        EngagementInfo? engagement = await PfaEngagements.InfoAsync(db, command.PfaId, cancellationToken);
        if (engagement is null)
        {
            return Result.Failure<AccountingPeriodDto>(AccountingErrors.PfaNotFound);
        }

        var start = DateOnly.ParseExact(command.Period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateOnly end = start.AddMonths(1).AddDays(-1);
        if (end >= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Result.Failure<AccountingPeriodDto>(PeriodErrors.NotEnded);
        }

        if (end < engagement.StartDate || engagement.EndDate is { } finished && start > finished)
        {
            return Result.Failure<AccountingPeriodDto>(PeriodErrors.OutsideEngagement);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, command.PfaId, command.Period, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<AccountingPeriodDto>(writable.Error.Code == "Accounting.PeriodClosed" ? PeriodErrors.AlreadyClosed(command.Period) : writable.Error);
        }

        // §8: „Închide luna” doar cu toate controalele trecute — verificat aici, nu doar în interfață.
        MonthReconciliationDto reconciliation = await MonthReconciliation.BuildAsync(
            db, command.PfaId, command.Period, options?.Value ?? new AccountingOptions(), DateTime.UtcNow, cancellationToken);
        if (!reconciliation.CanClose)
        {
            return Result.Failure<AccountingPeriodDto>(PeriodErrors.NotReconciled(reconciliation.Controls.Where(c => !c.Passed).Select(c => c.Detail)));
        }

        PfaAccountingPeriod period = await db.PfaAccountingPeriods.FirstOrDefaultAsync(p => p.PfaRegistrationId == command.PfaId && p.Period == command.Period, cancellationToken)
            ?? db.PfaAccountingPeriods.Add(new PfaAccountingPeriod { Id = Guid.NewGuid(), PfaRegistrationId = command.PfaId, Period = command.Period }).Entity;
        period.Status = AccountingPeriodStatus.Closed;
        period.ClosedByUserId = userContext.UserId;
        period.ClosedAtUtc = DateTime.UtcNow;

        List<LedgerEntry> entries = await db.LedgerEntries
            .Where(e => e.PfaRegistrationId == command.PfaId && e.AccountingPeriod == command.Period && e.Status != LedgerEntryStatus.Locked && !e.ClosedPeriodFlag)
            .ToListAsync(cancellationToken);
        entries.ForEach(e => e.Status = LedgerEntryStatus.Locked);
        List<DepreciationLine> depreciation = await db.DepreciationLines
            .Where(l => l.PfaRegistrationId == command.PfaId && l.Year == start.Year && l.Month == start.Month && !l.IsLocked)
            .ToListAsync(cancellationToken);
        depreciation.ForEach(l => l.IsLocked = true);
        await SnapshotAsync(command.PfaId, command.Period, start, end, cancellationToken);

        AccountingAudit.Record(
            db, command.PfaId, nameof(PfaAccountingPeriod), period.Id, "CLOSE",
            new { status = AccountingPeriodStatus.Open }, new { status = AccountingPeriodStatus.Closed, lockedEntries = entries.Count }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);

        Dictionary<Guid, UserRef> users = await PlatformDocumentSupport.UsersAsync(db, [userContext.UserId], cancellationToken);
        return new AccountingPeriodDto(command.PfaId, command.Period, period.Status, users.GetValueOrDefault(userContext.UserId), period.ClosedAtUtc);
    }

    private async Task SnapshotAsync(Guid pfaId, string period, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        Guid? pdf = null;
        if (rjipExport is not null && files is not null)
        {
            Result<Registers.RegisterFile> file = await rjipExport.Handle(new Registers.ExportRjipQuery(pfaId, start, end, Registers.RegisterFormat.Pdf), cancellationToken);
            if (file.IsSuccess)
            {
                pdf = (await files.StoreAsync(pfaId, file.Value.Content, $"RJIP-{period}.pdf", file.Value.ContentType, cancellationToken)).Id;
            }
        }

        await PeriodSnapshots.AddAsync(db, pfaId, period, options?.Value.ManualChannelMapping, pdf, userContext.UserId, cancellationToken);
    }
}

/// <summary>
/// Registrele lunii, înghețate la închidere (§7): RJIP-ul lunii și REF-ul anului până la sfârșitul ei,
/// ca date, plus PDF-ul RJIP când exportul e disponibil. Un snapshot nou nu îl șterge pe cel vechi.
/// </summary>
internal static class PeriodSnapshots
{
    public static async Task AddAsync(
        IApplicationDbContext db, Guid pfaId, string period, ManualChannelMapping? manual, Guid? pdf, Guid userId, CancellationToken cancellationToken)
    {
        var start = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateOnly end = start.AddMonths(1).AddDays(-1);
        List<Registers.RegisterEntry> entries = await Registers.RegisterData.EntriesAsync(db, pfaId, start, end, cancellationToken);
        RjipView rjip = Registers.GetRjipQueryHandler.Build(pfaId, start, end, entries, manual ?? ManualChannelMapping.OwnerContributionAndCash);
        RefView refView = await FiscalRegister.GetRefQueryHandler.ComputeAsync(db, pfaId, end.Year, RefStatus.Intermediate, end, cancellationToken);

        db.AccountingPeriodSnapshots.Add(new AccountingPeriodSnapshot
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfaId,
            Period = period,
            RjipJson = AccountingJson.Serialize(rjip),
            RefJson = AccountingJson.Serialize(refView),
            RjipPdfDocumentId = pdf,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
        });
    }
}

/// <summary><c>POST /accounting/pfas/{pfaId}/periods/{period}/corrections</c> — <c>{ ledgerEntryId?, change, reason }</c>.</summary>
public sealed record CreatePeriodCorrectionCommand(Guid PfaId, string Period, Guid? LedgerEntryId, JsonElement Change, string? Reason) : ICommand<PeriodCorrectionDto>;

/// <summary>
/// Corecția controlată a unei luni închise (spec contabilitate B8): doar ADMIN / ACCOUNTANT (grupul
/// de endpoint-uri), motiv obligatoriu, audit. O înregistrare blocată nu se modifică (spec flux
/// contabil §4): în luna curentă intră stornarea ei și înregistrarea corectată. Un import căzut în
/// luna închisă, încă neblocat, intră în lună și se blochează. Fără tranzacție, corecția rămâne o
/// notă în jurnalul lunii.
/// </summary>
internal sealed class CreatePeriodCorrectionCommandHandler(IApplicationDbContext db, IUserContext userContext, IDateTimeProvider? clock = null)
    : ICommandHandler<CreatePeriodCorrectionCommand, PeriodCorrectionDto>
{
    public async Task<Result<PeriodCorrectionDto>> Handle(CreatePeriodCorrectionCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<PeriodCorrectionDto>(PeriodErrors.CorrectionReasonRequired);
        }

        if (!PlatformDocumentSupport.IsValidPeriod(command.Period))
        {
            return Result.Failure<PeriodCorrectionDto>(AccountingErrors.InvalidPeriod);
        }

        if (await PfaEngagements.InfoAsync(db, command.PfaId, cancellationToken) is not { } engagement)
        {
            return Result.Failure<PeriodCorrectionDto>(AccountingErrors.PfaNotFound);
        }

        if (engagement.Status == EngagementStatus.Inactive)
        {
            return Result.Failure<PeriodCorrectionDto>(AccountingErrors.PfaReadOnly);
        }

        bool closed = await db.PfaAccountingPeriods.AnyAsync(
            p => p.PfaRegistrationId == command.PfaId && p.Period == command.Period && p.Status == AccountingPeriodStatus.Closed, cancellationToken);
        if (!closed)
        {
            return Result.Failure<PeriodCorrectionDto>(PeriodErrors.StillOpen(command.Period));
        }

        string reason = command.Reason.Trim();
        var entries = new StornoIds(null, null);
        if (command.LedgerEntryId is { } entryId)
        {
            Result<StornoIds> corrected = await CorrectEntryAsync(entryId, command, reason, cancellationToken);
            if (corrected.IsFailure)
            {
                return Result.Failure<PeriodCorrectionDto>(corrected.Error);
            }

            entries = corrected.Value;
        }

        var correction = new PeriodCorrection
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = command.PfaId,
            Period = command.Period,
            LedgerEntryId = command.LedgerEntryId,
            ChangeJson = command.Change.ValueKind == JsonValueKind.Undefined ? "{}" : command.Change.GetRawText(),
            Reason = reason,
            CreatedByUserId = userContext.UserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.PeriodCorrections.Add(correction);
        await db.SaveChangesAsync(cancellationToken);
        if (entries.Storno is null && command.LedgerEntryId is not null)
        {
            // Un import căzut în luna închisă a intrat acum în ea: luna are un snapshot nou (registre §3).
            await PeriodSnapshots.AddAsync(db, command.PfaId, command.Period, null, null, userContext.UserId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        Dictionary<Guid, UserRef> users = await PlatformDocumentSupport.UsersAsync(db, [userContext.UserId], cancellationToken);
        return new PeriodCorrectionDto(
            correction.Id,
            command.PfaId,
            command.Period,
            correction.LedgerEntryId,
            JsonDocument.Parse(correction.ChangeJson).RootElement.Clone(),
            reason,
            users.GetValueOrDefault(userContext.UserId) ?? new UserRef(userContext.UserId, string.Empty),
            correction.CreatedAtUtc,
            entries.Storno,
            entries.Replacement);
    }

    private async Task<Result<StornoIds>> CorrectEntryAsync(Guid entryId, CreatePeriodCorrectionCommand command, string reason, CancellationToken cancellationToken)
    {
        LedgerEntry? entry = await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken);
        if (entry is null || entry.PfaRegistrationId != command.PfaId || entry.AccountingPeriod != command.Period)
        {
            return Result.Failure<StornoIds>(PeriodErrors.EntryOutsidePeriod);
        }

        // Data se verifică înainte de orice modificare: o corecție nu mută tranzacția din lună.
        if (command.Change.ValueKind == JsonValueKind.Object &&
            command.Change.TryGetProperty("date", out JsonElement date) &&
            (date.ValueKind != JsonValueKind.String ||
             !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly moved) ||
             LedgerSupport.PeriodOf(moved) != command.Period))
        {
            return Result.Failure<StornoIds>(PeriodErrors.CorrectionMovesPeriod);
        }

        if (entry.Status == LedgerEntryStatus.Locked)
        {
            bool changes = command.Change.ValueKind == JsonValueKind.Object && command.Change.EnumerateObject().Any();
            return changes ? await StornoAsync(entry, command.Change, reason, cancellationToken) : new StornoIds(null, null);
        }

        var before = new Dictionary<string, object?>();
        var after = new Dictionary<string, object?>();
        if (command.Change.ValueKind == JsonValueKind.Object && command.Change.EnumerateObject().Any())
        {
            Result applied = await LedgerChanges.ApplyAllAsync(db, entry, command.Change, before, after, cancellationToken);
            if (applied.IsFailure && applied.Error != AccountingErrors.NoChanges)
            {
                return Result.Failure<StornoIds>(applied.Error);
            }
        }

        if (entry.ClosedPeriodFlag)
        {
            // Importul căzut în luna închisă intră acum în lună, prin corecție.
            before["closedPeriodFlag"] = true;
            after["closedPeriodFlag"] = false;
            entry.ClosedPeriodFlag = false;
        }

        before["status"] = entry.Status;
        after["status"] = LedgerEntryStatus.Locked;
        entry.Status = LedgerEntryStatus.Locked;
        DeductibilityService.Resolve(entry, await LedgerSupport.RulesAsync(db, entry.PfaRegistrationId, cancellationToken));
        Result valid = await LedgerSupport.ValidateAsync(db, entry, cancellationToken);
        if (valid.IsFailure)
        {
            return Result.Failure<StornoIds>(valid.Error);
        }

        AccountingAudit.Record(db, entry.PfaRegistrationId, nameof(LedgerEntry), entry.Id, "PERIOD_CORRECTION", before, after, reason, userContext.UserId);
        return new StornoIds(null, null);
    }

    /// <summary>
    /// Stornarea înregistrării blocate și înlocuitoarea ei corectată, amândouă în luna curentă. Legăturile
    /// cu extrasul, factura și decontarea rămân pe original: suma și canalul unei plăți bancare nu se schimbă.
    /// </summary>
    private async Task<Result<StornoIds>> StornoAsync(LedgerEntry entry, JsonElement change, string reason, CancellationToken cancellationToken)
    {
        if (await db.LedgerEntries.AnyAsync(e => e.StornoOfEntryId == entry.Id, cancellationToken))
        {
            return Result.Failure<StornoIds>(PeriodErrors.AlreadyStorned);
        }

        var today = DateOnly.FromDateTime(clock?.UtcNow ?? DateTime.UtcNow);
        string period = LedgerSupport.PeriodOf(today);
        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, entry.PfaRegistrationId, period, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<StornoIds>(writable.Error);
        }

        LedgerEntry replacement = Copy(entry);
        replacement.CorrectsEntryId = entry.Id;
        replacement.Status = LedgerEntryStatus.Verified;
        var before = new Dictionary<string, object?>();
        var after = new Dictionary<string, object?>();
        Result applied = await LedgerChanges.ApplyAllAsync(db, replacement, change, before, after, cancellationToken);
        if (applied.IsFailure)
        {
            return Result.Failure<StornoIds>(applied.Error);
        }

        if (entry.BankTransactionId is not null && (replacement.Amount != entry.Amount || replacement.PaymentMethod != entry.PaymentMethod))
        {
            return Result.Failure<StornoIds>(PeriodErrors.BankFacts);
        }

        // Deductibilitatea se stabilește la data operațiunii; înregistrarea intră apoi în luna curentă.
        DeductibilityService.Resolve(replacement, await LedgerSupport.RulesAsync(db, entry.PfaRegistrationId, cancellationToken));
        replacement.DocumentDate = after.ContainsKey("date") ? replacement.Date : entry.DocumentDate ?? entry.Date;
        replacement.Date = today;
        replacement.AccountingPeriod = period;

        LedgerEntry storno = Copy(entry);
        storno.StornoOfEntryId = entry.Id;
        storno.Status = LedgerEntryStatus.Locked;
        storno.Amount = -entry.Amount;
        storno.DeductibleAmount = -entry.DeductibleAmount;
        storno.DocumentLabel = $"Stornare {entry.DocumentLabel}".Trim();
        storno.Description = $"Stornare: {entry.Description}";
        storno.DocumentDate = entry.DocumentDate ?? entry.Date;
        storno.Date = today;
        storno.AccountingPeriod = period;

        foreach (LedgerEntry added in new[] { storno, replacement })
        {
            Result valid = await LedgerSupport.ValidateAsync(db, added, cancellationToken);
            if (valid.IsFailure)
            {
                return Result.Failure<StornoIds>(valid.Error);
            }

            db.LedgerEntries.Add(added);
        }

        after["stornoEntryId"] = storno.Id;
        after["replacementEntryId"] = replacement.Id;
        AccountingAudit.Record(db, entry.PfaRegistrationId, nameof(LedgerEntry), entry.Id, "PERIOD_CORRECTION", before, after, reason, userContext.UserId);
        return new StornoIds(storno.Id, replacement.Id);
    }

    /// <summary>Operațiunea, fără legăturile care îi aparțin doar originalului (extras, factură, decontare, import).</summary>
    private LedgerEntry Copy(LedgerEntry entry) => new()
    {
        Id = Guid.NewGuid(),
        PfaRegistrationId = entry.PfaRegistrationId,
        Date = entry.Date,
        DocumentLabel = entry.DocumentLabel,
        SourceDocumentId = entry.SourceDocumentId,
        Source = LedgerSource.Manual,
        Counterparty = entry.Counterparty,
        Description = entry.Description,
        TransactionType = entry.TransactionType,
        PaymentMethod = entry.PaymentMethod,
        Amount = entry.Amount,
        Currency = entry.Currency,
        Category = entry.Category,
        VehicleRelated = entry.VehicleRelated,
        DeductibilityType = entry.DeductibilityType,
        DeductiblePercent = entry.DeductiblePercent,
        DeductibleAmount = entry.DeductibleAmount,
        DeductibilitySettingId = entry.DeductibilitySettingId,
        DeductibilityRuleId = entry.DeductibilityRuleId,
        DeductibilityValidFrom = entry.DeductibilityValidFrom,
        ReconciliationStatus = entry.ReconciliationStatus,
        PersonalAmount = entry.PersonalAmount,
        FixedAssetReview = entry.FixedAssetReview,
        CreatedByUserId = userContext.UserId,
        CreatedAtUtc = DateTime.UtcNow,
    };

    private sealed record StornoIds(Guid? Storno, Guid? Replacement);
}

/// <summary><c>POST /accounting/pfas/{pfaId}/periods/{period}/reopen</c> — doar ADMIN, cu motiv.</summary>
public sealed record ReopenPeriodCommand(Guid PfaId, string Period, string? Reason) : ICommand<AccountingPeriodDto>;

/// <summary>
/// Redeschiderea unei luni închise (§8): doar ADMIN (permisiunea o pune ruta), cu motiv obligatoriu și
/// audit. Înregistrările blocate redevin verificate, deci modificabile; snapshot-ul închiderii rămâne ca
/// istoric.
/// </summary>
internal sealed class ReopenPeriodCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<ReopenPeriodCommand, AccountingPeriodDto>
{
    public async Task<Result<AccountingPeriodDto>> Handle(ReopenPeriodCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<AccountingPeriodDto>(PeriodErrors.ReopenReasonRequired);
        }

        PfaAccountingPeriod? period = await db.PfaAccountingPeriods
            .SingleOrDefaultAsync(p => p.PfaRegistrationId == command.PfaId && p.Period == command.Period, cancellationToken);
        if (period is not { Status: AccountingPeriodStatus.Closed })
        {
            return Result.Failure<AccountingPeriodDto>(PeriodErrors.NotClosed(command.Period));
        }

        int year = int.Parse(command.Period[..4], CultureInfo.InvariantCulture);
        if (await db.AccountingYears.AnyAsync(y => y.PfaRegistrationId == command.PfaId && y.Year == year && y.Status == AccountingPeriodStatus.Closed, cancellationToken))
        {
            return Result.Failure<AccountingPeriodDto>(PeriodErrors.YearClosed(year));
        }

        period.Status = AccountingPeriodStatus.Open;
        period.ClosedAtUtc = null;
        period.ClosedByUserId = null;
        List<LedgerEntry> entries = await db.LedgerEntries
            .Where(e => e.PfaRegistrationId == command.PfaId && e.AccountingPeriod == command.Period && e.Status == LedgerEntryStatus.Locked)
            .ToListAsync(cancellationToken);
        entries.ForEach(e => e.Status = LedgerEntryStatus.Verified);
        int month = int.Parse(command.Period[5..], CultureInfo.InvariantCulture);
        List<DepreciationLine> depreciation = await db.DepreciationLines
            .Where(l => l.PfaRegistrationId == command.PfaId && l.Year == year && l.Month == month && l.IsLocked)
            .ToListAsync(cancellationToken);
        depreciation.ForEach(l => l.IsLocked = false);

        AccountingAudit.Record(
            db, command.PfaId, nameof(PfaAccountingPeriod), period.Id, "REOPEN",
            new { status = AccountingPeriodStatus.Closed }, new { status = AccountingPeriodStatus.Open, unlockedEntries = entries.Count },
            command.Reason.Trim(), userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return new AccountingPeriodDto(command.PfaId, command.Period, period.Status, null, null);
    }
}

