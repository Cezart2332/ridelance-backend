using System.Globalization;
using System.Linq.Expressions;
using Application.Abstractions.Data;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace Application.Accounting.Ledger;

/// <summary>Ce au în comun importatorii și comenzile ledger-ului.</summary>
internal static class LedgerSupport
{
    public const int DocumentLabelLength = 128;
    public const int CounterpartyLength = 256;
    public const int DescriptionLength = 500;

    public static string PeriodOf(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static async Task<LedgerRules> RulesAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken) => new(
        await db.ExpenseCategoryRules.AsNoTracking().ToListAsync(cancellationToken),
        await db.PfaAccountingSettings.AsNoTracking()
            .Where(s => s.PfaRegistrationId == pfaId && s.Key == PfaAccountingSettingKeys.VehicleDeductibility)
            .ToListAsync(cancellationToken));

    public static async Task<HashSet<string>> ClosedPeriodsAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken) =>
        [.. await db.PfaAccountingPeriods
            .Where(p => p.PfaRegistrationId == pfaId && p.Status == AccountingPeriodStatus.Closed)
            .Select(p => p.Period)
            .ToListAsync(cancellationToken)];

    /// <summary>
    /// O înregistrare nouă dintr-un import. Dacă luna e închisă, importul nu o modifică: intră la
    /// verificare, cu flag-ul „perioadă închisă” (B6), și nu contează în registre până la corecție.
    /// </summary>
    public static LedgerEntry New(
        Guid pfaId,
        DateOnly date,
        LedgerSource source,
        string? externalId,
        string documentLabel,
        string? counterparty,
        string description,
        LedgerTransactionType type,
        PaymentMethod method,
        decimal amount,
        string currency,
        LedgerEntryStatus status,
        IReadOnlySet<string> closedPeriods)
    {
        string period = PeriodOf(date);
        bool closed = closedPeriods.Contains(period);
        return new LedgerEntry
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfaId,
            Date = date,
            Source = source,
            ExternalId = externalId,
            DocumentLabel = Cut(documentLabel, DocumentLabelLength),
            Counterparty = string.IsNullOrWhiteSpace(counterparty) ? null : Cut(counterparty.Trim(), CounterpartyLength),
            Description = Cut(description, DescriptionLength),
            TransactionType = type,
            PaymentMethod = method,
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "RON" : currency.ToUpperInvariant(),
            Status = closed ? LedgerEntryStatus.NeedsReview : status,
            AccountingPeriod = period,
            ClosedPeriodFlag = closed,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    public static string Cut(string value, int length) => value.Length <= length ? value : value[..length];

    /// <summary>Înregistrările, cu versiunea rândului (<c>xmin</c>), în forma din contract.</summary>
    public static async Task<List<LedgerEntryDto>> DtosAsync(
        IQueryable<LedgerEntry> entries,
        CancellationToken cancellationToken)
    {
        var rows = await entries
            .Select(e => new { Entry = e, RowVersion = EF.Property<uint>(e, "xmin") })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => Dto(row.Entry, row.RowVersion))];
    }

    public static async Task<LedgerEntryDto> DtoAsync(IApplicationDbContext db, Guid id, CancellationToken cancellationToken) =>
        (await DtosAsync(db.LedgerEntries.AsNoTracking().Where(e => e.Id == id), cancellationToken)).Single();

    public static LedgerEntryDto Dto(LedgerEntry e, uint rowVersion) => new(
        e.Id,
        e.PfaRegistrationId,
        e.Date,
        e.DocumentLabel,
        e.SourceDocumentId,
        e.Source,
        e.ExternalId,
        e.Counterparty,
        e.Description,
        e.TransactionType,
        e.PaymentMethod,
        e.Amount,
        e.Currency,
        e.Category,
        e.VehicleRelated,
        e.DeductibilityType,
        e.DeductiblePercent,
        e.DeductibleAmount,
        RuleOf(e),
        e.Status,
        e.AccountingPeriod,
        e.ClosedPeriodFlag,
        rowVersion.ToString(CultureInfo.InvariantCulture));

    /// <summary>Regula sau setarea aplicată, pentru „sumă × % = deductibil”.</summary>
    private static DeductibilityRuleRef? RuleOf(LedgerEntry e)
    {
        if (e.DeductibilityValidFrom is not { } validFrom)
        {
            return null;
        }

        string? settingKey = e.DeductibilitySettingId is null ? null : PfaAccountingSettingKeys.VehicleDeductibility;
        return new DeductibilityRuleRef(settingKey, e.DeductibilityRuleId, validFrom);
    }

    /// <summary>Plățile din bancă fără document justificativ, pe care un document le-ar putea acoperi.</summary>
    public static Expression<Func<LedgerEntry, bool>> UndocumentedBankExpense(Guid pfaId) =>
        e => e.PfaRegistrationId == pfaId &&
             e.TransactionType == LedgerTransactionType.Expense &&
             e.PaymentMethod == PaymentMethod.Bank &&
             e.SourceDocumentId == null &&
             e.Status != LedgerEntryStatus.Locked &&
             !e.ClosedPeriodFlag;
}
