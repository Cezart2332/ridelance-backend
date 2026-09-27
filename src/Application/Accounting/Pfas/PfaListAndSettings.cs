using System.Globalization;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Application.Accounting.Ledger;
using Application.Accounting.Months;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Pfas;

/// <summary><c>GET /accounting/pfas?status=active|inactive&amp;search=</c></summary>
public sealed record ListPfasQuery(string? Status, string? Search) : IQuery<IReadOnlyList<PfaListItem>>;

/// <summary>
/// PFA-urile contabilității: cele cu o colaborare contabilă înregistrată și cele cu onboardingul
/// încheiat (cont activ). Lista se construiește dintr-un număr fix de interogări, nu câte una pe PFA.
/// </summary>
internal sealed class ListPfasQueryHandler(IApplicationDbContext db) : IQueryHandler<ListPfasQuery, IReadOnlyList<PfaListItem>>
{
    private static readonly CompareInfo Ro = CultureInfo.GetCultureInfo("ro-RO").CompareInfo;

    public async Task<Result<IReadOnlyList<PfaListItem>>> Handle(ListPfasQuery query, CancellationToken cancellationToken)
    {
        List<PfaAccountingEngagement> engagements = await db.PfaAccountingEngagements.AsNoTracking().ToListAsync(cancellationToken);
        List<Guid> engaged = [.. engagements.Select(e => e.PfaRegistrationId).Distinct()];
        var rows = await db.PfaRegistrations.AsNoTracking()
            .Where(p => engaged.Contains(p.Id) || p.OnboardingCompletedAtUtc != null && p.User.DeletedAtUtc == null)
            .Select(p => new { p.Id, p.LegalName, p.FullName, p.Cui, p.OnboardingCompletedAtUtc, p.CreatedAtUtc, p.User.FirstName, p.User.LastName, p.UserId, p.User.Email, p.User.PhoneNumber })
            .ToListAsync(cancellationToken);

        string search = Fold(query.Search);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        string period = PfaEngagements.CurrentPeriod(today);
        var candidates = rows
            .Select(p =>
            {
                PfaAccountingEngagement? engagement = engagements
                    .Where(e => e.PfaRegistrationId == p.Id)
                    .OrderByDescending(e => e.Status == EngagementStatus.Active)
                    .ThenByDescending(e => e.StartDate)
                    .FirstOrDefault();
                string name = (p.LegalName ?? p.FullName ?? $"{p.FirstName} {p.LastName}").Trim();
                return new { p.Id, Name = name, Cui = p.Cui ?? string.Empty, Status = engagement?.Status ?? EngagementStatus.Active, Client = new ClientContact(p.UserId, p.Email, p.PhoneNumber) };
            })
            .Where(p => query.Status?.ToUpperInvariant() switch
            {
                "ACTIVE" => p.Status == EngagementStatus.Active,
                "INACTIVE" => p.Status == EngagementStatus.Inactive,
                _ => true,
            })
            .Where(p => search.Length == 0 || Fold(p.Name).Contains(search, StringComparison.Ordinal) || p.Cui.Contains(search, StringComparison.Ordinal))
            .ToList();

        List<Guid> ids = [.. candidates.Select(p => p.Id)];
        MonthData data = await MonthData.LoadAsync(db, period, ids, cancellationToken);
        Dictionary<Guid, PfaMonthStatus> months = await db.PfaMonthChecks.AsNoTracking()
            .Where(c => c.Period == period && ids.Contains(c.PfaRegistrationId))
            .ToDictionaryAsync(c => c.PfaRegistrationId, c => c.Status, cancellationToken);
        Dictionary<Guid, CashRegisterStatus> cash = await db.CashRegisterStates.AsNoTracking()
            .Where(c => ids.Contains(c.PfaRegistrationId))
            .ToDictionaryAsync(c => c.PfaRegistrationId, c => c.Status, cancellationToken);

        return candidates
            .OrderBy(p => p.Name, Comparer<string>.Create((a, b) => Ro.Compare(a, b, CompareOptions.IgnoreCase)))
            .Select(p => new PfaListItem(
                p.Id,
                p.Name,
                p.Cui,
                data.Art317Of(p.Id).Where(a => a.ValidFrom <= today).OrderByDescending(a => a.ValidFrom).FirstOrDefault() is { Enabled: true },
                data.PlatformsOf(p.Id) ?? [],
                p.Status,
                period,
                months.GetValueOrDefault(p.Id, PfaMonthStatus.NotProcessed),
                cash.GetValueOrDefault(p.Id, CashRegisterStatus.NotRequiredCurrentConfiguration),
                p.Client))
            .ToList();
    }

    /// <summary>Căutare fără diacritice și fără majuscule („ștefan” găsește „Stefan”).</summary>
    private static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (char c in value.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}

/// <summary><c>GET /accounting/pfas/{pfaId}/settings</c></summary>
public sealed record GetPfaSettingsQuery(Guid PfaId) : IQuery<PfaAccountingSettingsDto>;

internal sealed class GetPfaSettingsQueryHandler(IApplicationDbContext db) : IQueryHandler<GetPfaSettingsQuery, PfaAccountingSettingsDto>
{
    public async Task<Result<PfaAccountingSettingsDto>> Handle(GetPfaSettingsQuery query, CancellationToken cancellationToken)
    {
        PfaAccountingSettingsDto? settings = await PfaSettings.BuildAsync(db, query.PfaId, cancellationToken);
        return settings is null ? Result.Failure<PfaAccountingSettingsDto>(AccountingErrors.PfaNotFound) : settings;
    }
}

/// <summary><c>PUT /accounting/pfas/{pfaId}/settings</c> — <c>{ field, value, validFrom, note }</c>.</summary>
public sealed record UpdatePfaSettingsCommand(Guid PfaId, SettingsChange Change) : ICommand<PfaAccountingSettingsDto>;

/// <summary>
/// Setările contabile sunt append-only, cu „Valabil de la” (spec contabilitate F5, B0): o valoare
/// nouă închide intervalul celei vechi, nu o suprascrie. Deductibilitatea auto se reaplică pe
/// cheltuielile din lunile deschise de la data ei; pre-check-ul lunii curente se reface.
/// </summary>
internal sealed class UpdatePfaSettingsCommandHandler(IApplicationDbContext db, IUserContext userContext, Microsoft.Extensions.Options.IOptions<AccountingOptions> options)
    : ICommandHandler<UpdatePfaSettingsCommand, PfaAccountingSettingsDto>
{
    public async Task<Result<PfaAccountingSettingsDto>> Handle(UpdatePfaSettingsCommand command, CancellationToken cancellationToken)
    {
        SettingsChange change = command.Change;
        if (string.IsNullOrWhiteSpace(change.Note))
        {
            return Result.Failure<PfaAccountingSettingsDto>(PfaSettings.NoteRequired);
        }

        Result<string> value = PfaSettings.Normalize(change.Field, change.Value);
        if (value.IsFailure)
        {
            return Result.Failure<PfaAccountingSettingsDto>(value.Error);
        }

        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == command.PfaId, cancellationToken))
        {
            return Result.Failure<PfaAccountingSettingsDto>(AccountingErrors.PfaNotFound);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, command.PfaId, string.Empty, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<PfaAccountingSettingsDto>(writable.Error);
        }

        if (await db.PfaAccountingSettings.AnyAsync(s => s.PfaRegistrationId == command.PfaId && s.Key == change.Field && s.ValidFrom == change.ValidFrom, cancellationToken))
        {
            return Result.Failure<PfaAccountingSettingsDto>(PfaSettings.Exists(change.ValidFrom));
        }

        var setting = new PfaAccountingSetting
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = command.PfaId,
            Key = change.Field,
            ValueJson = value.Value,
            ValidFrom = change.ValidFrom,
            Note = change.Note.Trim(),
            ChangedByUserId = userContext.UserId,
            ChangedAtUtc = DateTime.UtcNow,
        };
        db.PfaAccountingSettings.Add(setting);
        AccountingAudit.Record(
            db, command.PfaId, nameof(PfaAccountingSetting), setting.Id, "APPEND", null,
            new { key = change.Field, value = JsonDocument.Parse(value.Value).RootElement, validFrom = change.ValidFrom }, setting.Note, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);

        if (change.Field == PfaAccountingSettingKeys.VehicleDeductibility)
        {
            await ReapplyDeductibilityAsync(command.PfaId, change.ValidFrom, cancellationToken);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await PreCheck.RefreshIfProcessedAsync(db, command.PfaId, PfaEngagements.CurrentPeriod(today), options.Value, cancellationToken);
        return (await PfaSettings.BuildAsync(db, command.PfaId, cancellationToken))!;
    }

    private async Task ReapplyDeductibilityAsync(Guid pfaId, DateOnly from, CancellationToken cancellationToken)
    {
        HashSet<string> closed = await LedgerSupport.ClosedPeriodsAsync(db, pfaId, cancellationToken);
        List<LedgerEntry> entries = await db.LedgerEntries
            .Where(e => e.PfaRegistrationId == pfaId && e.Date >= from && e.TransactionType == LedgerTransactionType.Expense && e.Status != LedgerEntryStatus.Locked)
            .ToListAsync(cancellationToken);
        LedgerRules rules = await LedgerSupport.RulesAsync(db, pfaId, cancellationToken);
        foreach (LedgerEntry entry in entries.Where(e => !closed.Contains(e.AccountingPeriod)))
        {
            DeductibilityService.Resolve(entry, rules);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal static class PfaSettings
{
    public static readonly Error NoteRequired = Error.Problem("Accounting.ReasonRequired", "Observația / justificarea e obligatorie.");

    public static readonly Error PlatformsRequired = Error.Problem("Accounting.PlatformsRequired", "Alege cel puțin o platformă.");

    public static Error Exists(DateOnly validFrom) => Error.Conflict(
        "Accounting.SettingExists",
        $"Există deja o valoare cu „Valabil de la” {validFrom.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}. Istoricul nu se suprascrie.");

    /// <summary>Valoarea unei setări, validată după cheie și scrisă în forma ei JSON.</summary>
    public static Result<string> Normalize(string field, JsonElement value)
    {
        try
        {
            switch (field)
            {
                case PfaAccountingSettingKeys.Art317 when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    return AccountingJson.Serialize(value.GetBoolean());
                case PfaAccountingSettingKeys.Platforms:
                    List<Platform> platforms = [.. (value.Deserialize<List<Platform>>(AccountingJson.Options) ?? []).Distinct()];
                    return platforms.Count == 0 ? Result.Failure<string>(PlatformsRequired) : AccountingJson.Serialize(platforms);
                case PfaAccountingSettingKeys.VehicleDeductibility:
                    DeductibilityType type = value.Deserialize<DeductibilityType>(AccountingJson.Options);
                    return type is DeductibilityType.Percent50 or DeductibilityType.Percent100
                        ? AccountingJson.Serialize(type)
                        : Result.Failure<string>(AccountingErrors.InvalidField("value"));
                case PfaAccountingSettingKeys.Art317:
                    return Result.Failure<string>(AccountingErrors.InvalidField("value"));
                default:
                    return Result.Failure<string>(AccountingErrors.InvalidField("field"));
            }
        }
        catch (JsonException)
        {
            return Result.Failure<string>(AccountingErrors.InvalidField("value"));
        }
    }

    /// <summary>Valorile de azi și istoricul, cu „valabil până la” derivat din intrarea următoare.</summary>
    public static async Task<PfaAccountingSettingsDto?> BuildAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken)
    {
        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == pfaId, cancellationToken))
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        List<PfaAccountingSetting> settings = await db.PfaAccountingSettings.AsNoTracking()
            .Where(s => s.PfaRegistrationId == pfaId)
            .OrderBy(s => s.Key)
            .ThenBy(s => s.ValidFrom)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, UserRef> users = await PlatformDocumentSupport.UsersAsync(db, settings.Select(s => (Guid?)s.ChangedByUserId), cancellationToken);
        MonthData data = await MonthData.LoadAsync(db, PfaEngagements.CurrentPeriod(today), [pfaId], cancellationToken);

        PfaAccountingSetting? At(string key) => settings
            .Where(s => s.Key == key && s.ValidFrom <= today)
            .OrderByDescending(s => s.ValidFrom)
            .FirstOrDefault();

        PfaAccountingSetting? art317 = At(PfaAccountingSettingKeys.Art317);
        bool art317Enabled = art317 is not null && AccountingJson.Deserialize(art317.ValueJson, false);
        DeductibilityType vehicle = At(PfaAccountingSettingKeys.VehicleDeductibility) is { } deductibility
            ? AccountingJson.Deserialize(deductibility.ValueJson, DeductibilityType.Percent50)
            : DeductibilityType.Percent50;

        List<SettingHistoryEntry> history = [.. settings.Select((s, index) =>
        {
            PfaAccountingSetting? next = index + 1 < settings.Count && settings[index + 1].Key == s.Key ? settings[index + 1] : null;
            return new SettingHistoryEntry(
                s.Id,
                s.Key,
                JsonDocument.Parse(s.ValueJson).RootElement.Clone(),
                s.ValidFrom,
                next?.ValidFrom.AddDays(-1),
                s.Note,
                users.TryGetValue(s.ChangedByUserId, out UserRef? user) ? user : new UserRef(s.ChangedByUserId, string.Empty),
                s.ChangedAtUtc);
        })];

        PfaAccountingSummary summary = (await PfaSummaries.BuildAsync(db, pfaId, today, cancellationToken))!;
        return new PfaAccountingSettingsDto(
            pfaId,
            RealSystem: true,
            VatPayer: false,
            new Art317Setting(art317Enabled, art317Enabled ? art317!.ValidFrom : null),
            data.PlatformsOf(pfaId) ?? [],
            vehicle,
            summary.Cash,
            history);
    }
}
