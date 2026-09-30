using System.Globalization;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Registers;
using Domain.Accounting;
using Domain.Banking;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Periods;

/// <summary>Controalele reconcilierii lunare (spec flux contabil §8).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(UpperSnakeCaseEnumConverter<ReconciliationControl>))]
public enum ReconciliationControl
{
    OpenBanking = 0,
    EFactura = 1,
    CashRegister = 2,
    BoltDocuments = 3,
    UberDocuments = 4,
    UnreconciledPayouts = 5,
    OpenTransactions = 6,
    PlatformCashVsZ = 7,
    BankBalance = 8,
}

/// <param name="Applicable">Fals când controlul nu privește PFA-ul (fără casă de marcat, fără Uber): trece.</param>
public sealed record ReconciliationControlDto(ReconciliationControl Control, bool Passed, bool Applicable, string Detail);

/// <summary>Un payout al lunii: suma virată, venitul brut și comisionul asociate, diferența.</summary>
public sealed record PayoutReconciliationDto(
    Guid BankTransactionId,
    DateOnly Date,
    LedgerSource Platform,
    decimal Payout,
    decimal? Gross,
    decimal? Commission,
    decimal? Difference,
    ReconciliationStatus Status);

public sealed record MonthReconciliationDto(
    Guid PfaId,
    string Period,
    AccountingPeriodStatus Status,
    bool CanClose,
    IReadOnlyList<ReconciliationControlDto> Controls,
    IReadOnlyList<PayoutReconciliationDto> Payouts);

/// <summary><c>GET /accounting/pfas/{pfaId}/periods/{period}/reconciliation</c></summary>
public sealed record GetMonthReconciliationQuery(Guid PfaId, string Period) : IQuery<MonthReconciliationDto>;

internal sealed class GetMonthReconciliationQueryHandler(IApplicationDbContext db, IOptions<AccountingOptions>? options = null)
    : IQueryHandler<GetMonthReconciliationQuery, MonthReconciliationDto>
{
    public async Task<Result<MonthReconciliationDto>> Handle(GetMonthReconciliationQuery query, CancellationToken cancellationToken)
    {
        if (!Documents.PlatformDocumentSupport.IsValidPeriod(query.Period))
        {
            return Result.Failure<MonthReconciliationDto>(AccountingErrors.InvalidPeriod);
        }

        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == query.PfaId, cancellationToken))
        {
            return Result.Failure<MonthReconciliationDto>(AccountingErrors.PfaNotFound);
        }

        return await MonthReconciliation.BuildAsync(db, query.PfaId, query.Period, options?.Value ?? new AccountingOptions(), DateTime.UtcNow, cancellationToken);
    }
}

/// <summary>
/// Checklist-ul unei luni (§8): fiecare sursă și control, cu ce lipsește. „Închide luna” merge doar cu
/// toate trecute; aceeași verificare o face și serverul la închidere.
/// </summary>
internal static class MonthReconciliation
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static async Task<MonthReconciliationDto> BuildAsync(
        IApplicationDbContext db, Guid pfaId, string period, AccountingOptions options, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var start = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateOnly end = start.AddMonths(1).AddDays(-1);
        var pfa = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.Id == pfaId)
            .Select(p => new { p.UserId })
            .SingleAsync(cancellationToken);

        List<LedgerEntry> entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfaId && e.Date >= start && e.Date <= end && !e.ClosedPeriodFlag)
            .ToListAsync(cancellationToken);

        List<ReconciliationControlDto> controls =
        [
            await OpenBankingAsync(db, pfaId, pfa.UserId, nowUtc, cancellationToken),
            await EFacturaAsync(db, pfaId, end, cancellationToken),
            await CashRegisterAsync(db, pfaId, start, end, entries, cancellationToken),
            await PlatformDocumentsAsync(db, pfaId, period, Platform.Bolt, entries, cancellationToken),
            await PlatformDocumentsAsync(db, pfaId, period, Platform.Uber, entries, cancellationToken),
            UnreconciledPayouts(entries),
            await OpenTransactionsAsync(db, pfaId, start, end, entries, cancellationToken),
            await PlatformCashAsync(db, pfaId, period, start, end, cancellationToken),
            await BankBalanceAsync(db, pfaId, pfa.UserId, start, end, entries, options, cancellationToken),
        ];

        AccountingPeriodStatus status = await db.PfaAccountingPeriods.AsNoTracking()
            .Where(p => p.PfaRegistrationId == pfaId && p.Period == period)
            .Select(p => (AccountingPeriodStatus?)p.Status)
            .SingleOrDefaultAsync(cancellationToken) ?? AccountingPeriodStatus.Open;

        return new MonthReconciliationDto(pfaId, period, status, controls.All(c => c.Passed), controls, Payouts(entries));
    }

    private static string Lei(decimal value) => value.ToString("#,##0.00", Ro) + " lei";

    /// <summary>Open Banking: sincronizare reușită în ultimele 24 de ore, cu consimțământul valabil.</summary>
    private static async Task<ReconciliationControlDto> OpenBankingAsync(IApplicationDbContext db, Guid pfaId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        Guid? declared = await db.PfaBankAccountDeclarations.AsNoTracking()
            .Where(d => d.PfaRegistrationId == pfaId)
            .Select(d => d.BankConnectionId)
            .FirstOrDefaultAsync(cancellationToken);
        BankConnection? connection = await db.BankConnections.AsNoTracking()
            .Where(c => declared != null ? c.Id == declared : c.UserId == userId)
            .OrderByDescending(c => c.Status == BankConnectionStatus.Linked)
            .ThenByDescending(c => c.LastSyncedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        (bool passed, string detail) = connection switch
        {
            null => (false, "Banca nu e conectată."),
            { Status: not BankConnectionStatus.Linked } => (false, "Conexiunea bancară nu e activă."),
            { ConsentExpiresAtUtc: { } expires } when expires <= nowUtc => (false, "Consimțământul bancar a expirat."),
            { LastSyncedAtUtc: null } => (false, "Contul n-a fost sincronizat încă."),
            { LastSyncedAtUtc: { } synced } when synced < nowUtc.AddHours(-24) => (false, $"Ultima sincronizare: {synced:dd.MM.yyyy HH:mm} UTC, acum mai mult de 24 de ore."),
            _ => (true, $"Sincronizat {connection.LastSyncedAtUtc:dd.MM.yyyy HH:mm} UTC."),
        };
        return new ReconciliationControlDto(ReconciliationControl.OpenBanking, passed, true, detail);
    }

    /// <summary>e-Factura: importul SPV a rulat după sfârșitul lunii, deci o acoperă întreagă.</summary>
    private static async Task<ReconciliationControlDto> EFacturaAsync(IApplicationDbContext db, Guid pfaId, DateOnly end, CancellationToken cancellationToken)
    {
        AnafPfaLink? link = await db.AnafPfaLinks.AsNoTracking().SingleOrDefaultAsync(l => l.PfaRegistrationId == pfaId, cancellationToken);
        var covered = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        (bool passed, string detail) = link switch
        {
            null => (false, "e-Factura nu e conectată pentru acest PFA."),
            { Status: not AnafPfaLinkStatus.Active } => (false, "Conexiunea e-Factura nu e activă."),
            { LastSyncAtUtc: { } synced } when synced >= covered => (true, $"Import SPV la {synced:dd.MM.yyyy HH:mm} UTC."),
            _ => (false, "Importul SPV n-a rulat după sfârșitul lunii."),
        };
        return new ReconciliationControlDto(ReconciliationControl.EFactura, passed, true, detail);
    }

    /// <summary>Casa de marcat: un Z pentru fiecare zi cu bonuri, iar bonurile dau totalul Z-ului (R12).</summary>
    private static async Task<ReconciliationControlDto> CashRegisterAsync(
        IApplicationDbContext db, Guid pfaId, DateOnly start, DateOnly end, List<LedgerEntry> entries, CancellationToken cancellationToken)
    {
        CashRegisterState? cash = await db.CashRegisterStates.AsNoTracking().FirstOrDefaultAsync(c => c.PfaRegistrationId == pfaId, cancellationToken);
        if (cash is not { Status: CashRegisterStatus.Active, ActivationDate: { } activated } || activated > end)
        {
            return new ReconciliationControlDto(ReconciliationControl.CashRegister, true, false, "Fără casă de marcat în lună.");
        }

        List<DateOnly> receiptDays = await db.FiscalReceipts.AsNoTracking()
            .Where(r => r.PfaRegistrationId == pfaId && r.Date >= start && r.Date <= end)
            .Select(r => r.Date).Distinct().ToListAsync(cancellationToken);
        List<DateOnly> zDays = await db.ZReports.AsNoTracking()
            .Where(z => z.PfaRegistrationId == pfaId && z.Date >= start && z.Date <= end)
            .Select(z => z.Date).Distinct().ToListAsync(cancellationToken);
        List<DateOnly> missing = [.. receiptDays.Except(zDays).Order()];
        int mismatched = entries.Count(e => e.Source == LedgerSource.CashZ && e.ReconciliationStatus == ReconciliationStatus.NeedsReview);

        if (missing.Count > 0)
        {
            return new ReconciliationControlDto(ReconciliationControl.CashRegister, false, true,
                $"Lipsește raportul Z pentru {string.Join(", ", missing.Select(d => d.ToString("dd.MM", CultureInfo.InvariantCulture)))}.");
        }

        return mismatched > 0
            ? new ReconciliationControlDto(ReconciliationControl.CashRegister, false, true, $"{mismatched} rapoarte Z nu corespund bonurilor.")
            : new ReconciliationControlDto(ReconciliationControl.CashRegister, true, true, $"{zDays.Count} rapoarte Z.");
    }

    /// <summary>Raportul fiscal și factura de comision ale platformei, încărcate și confirmate.</summary>
    private static async Task<ReconciliationControlDto> PlatformDocumentsAsync(
        IApplicationDbContext db, Guid pfaId, string period, Platform platform, List<LedgerEntry> entries, CancellationToken cancellationToken)
    {
        ReconciliationControl control = platform == Platform.Bolt ? ReconciliationControl.BoltDocuments : ReconciliationControl.UberDocuments;
        LedgerSource source = platform == Platform.Bolt ? LedgerSource.Bolt : LedgerSource.Uber;
        PfaPlatformProvider provider = platform == Platform.Bolt ? PfaPlatformProvider.Bolt : PfaPlatformProvider.Uber;
        var documents = await db.PlatformDocuments.AsNoTracking()
            .Where(d => d.PfaRegistrationId == pfaId && d.Platform == platform && d.Period == period && d.DeletedAtUtc == null)
            .Select(d => new { d.DocumentType, d.Status })
            .ToListAsync(cancellationToken);

        bool used = entries.Any(e => e.Source == source) || documents.Count > 0 ||
                    await db.PfaPlatformAccounts.AnyAsync(a => a.PfaRegistrationId == pfaId && a.Provider == provider && a.IsSelectedByUser, cancellationToken);
        if (!used)
        {
            return new ReconciliationControlDto(control, true, false, "Fără activitate pe platformă.");
        }

        bool Confirmed(PlatformDocumentType type) => documents.Any(d => d.DocumentType == type && d.Status is PlatformDocumentStatus.Confirmed or PlatformDocumentStatus.Locked);
        List<string> missing = [];
        if (!Confirmed(PlatformDocumentType.PlatformReport))
        {
            missing.Add("raportul fiscal");
        }

        if (!Confirmed(PlatformDocumentType.CommissionInvoice))
        {
            missing.Add("factura de comision");
        }

        return missing.Count == 0
            ? new ReconciliationControlDto(control, true, true, "Raportul și factura de comision sunt confirmate.")
            : new ReconciliationControlDto(control, false, true, $"Lipsește {string.Join(" și ", missing)} (încărcat și confirmat).");
    }

    /// <summary>R20/R22: niciun payout fără descompunere în venit brut și comision.</summary>
    private static ReconciliationControlDto UnreconciledPayouts(List<LedgerEntry> entries)
    {
        List<LedgerEntry> open = [.. entries.Where(e => e.TransactionType == LedgerTransactionType.PlatformSettlement)];
        return open.Count == 0
            ? new ReconciliationControlDto(ReconciliationControl.UnreconciledPayouts, true, true, "Toate payout-urile sunt reconciliate.")
            : new ReconciliationControlDto(ReconciliationControl.UnreconciledPayouts, false, true, $"{open.Count} payout-uri nereconciliate ({Lei(open.Sum(e => e.Amount))}).");
    }

    /// <summary>Tranzacțiile fără document sau de verificat, plus propunerile de asociere fără răspuns.</summary>
    private static async Task<ReconciliationControlDto> OpenTransactionsAsync(
        IApplicationDbContext db, Guid pfaId, DateOnly start, DateOnly end, List<LedgerEntry> entries, CancellationToken cancellationToken)
    {
        // Stornarea poartă starea originalului (pentru REF), dar nu e o tranzacție de lucrat.
        List<LedgerEntry> own = [.. entries.Where(e => e.StornoOfEntryId is null)];
        int unmatched = own.Count(e => e.ReconciliationStatus == ReconciliationStatus.Unmatched);
        int review = own.Count(e => e.ReconciliationStatus == ReconciliationStatus.NeedsReview);
        int proposals = await db.LedgerMatchProposals.AsNoTracking()
            .Where(p => p.PfaRegistrationId == pfaId && p.Accepted == null)
            .Join(db.BankTransactions, p => p.BankTransactionId, t => t.Id, (p, t) => t.BookingDate ?? t.ValueDate)
            .CountAsync(date => date >= start && date <= end, cancellationToken);

        List<string> parts = [];
        if (unmatched > 0)
        {
            parts.Add($"{unmatched} fără document");
        }

        if (review > 0)
        {
            parts.Add($"{review} de verificat");
        }

        if (proposals > 0)
        {
            parts.Add($"{proposals} asocieri de confirmat");
        }

        return parts.Count == 0
            ? new ReconciliationControlDto(ReconciliationControl.OpenTransactions, true, true, "Nicio tranzacție deschisă.")
            : new ReconciliationControlDto(ReconciliationControl.OpenTransactions, false, true, string.Join(", ", parts) + ".");
    }

    /// <summary>R24/R25: cash-ul raportat de platforme = suma rapoartelor Z din lună. Nu se adună.</summary>
    private static async Task<ReconciliationControlDto> PlatformCashAsync(
        IApplicationDbContext db, Guid pfaId, string period, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        List<decimal?> reported = await db.DocumentExtractions.AsNoTracking()
            .Where(e => e.IsCurrent && e.PlatformDocument.PfaRegistrationId == pfaId && e.PlatformDocument.Period == period &&
                        e.PlatformDocument.DeletedAtUtc == null && e.PlatformDocument.DocumentType == PlatformDocumentType.PlatformReport &&
                        (e.PlatformDocument.Status == PlatformDocumentStatus.Confirmed || e.PlatformDocument.Status == PlatformDocumentStatus.Locked))
            .Select(e => e.CashAmount)
            .ToListAsync(cancellationToken);
        decimal cash = reported.Sum(amount => amount ?? 0);
        decimal z = await db.ZReports.AsNoTracking()
            .Where(r => r.PfaRegistrationId == pfaId && r.Date >= start && r.Date <= end)
            .SumAsync(r => r.Total, cancellationToken);

        if (cash == 0 && z == 0)
        {
            return new ReconciliationControlDto(ReconciliationControl.PlatformCashVsZ, true, false, "Fără încasări numerar.");
        }

        decimal difference = cash - z;
        return difference == 0
            ? new ReconciliationControlDto(ReconciliationControl.PlatformCashVsZ, true, true, $"Cash raportat {Lei(cash)} = Z {Lei(z)}.")
            : new ReconciliationControlDto(ReconciliationControl.PlatformCashVsZ, false, true, $"Cash raportat {Lei(cash)}, Z {Lei(z)}: diferență {Lei(difference)}.");
    }

    /// <summary>
    /// §7: încasările minus plățile din coloana bancă a RJIP = variația contului conectat în lună (suma
    /// tranzacțiilor lui). Doar în lei.
    /// </summary>
    private static async Task<ReconciliationControlDto> BankBalanceAsync(
        IApplicationDbContext db, Guid pfaId, Guid userId, DateOnly start, DateOnly end, List<LedgerEntry> entries, AccountingOptions options, CancellationToken cancellationToken)
    {
        Guid? declared = await db.PfaBankAccountDeclarations.AsNoTracking()
            .Where(d => d.PfaRegistrationId == pfaId)
            .Select(d => d.BankConnectionId)
            .FirstOrDefaultAsync(cancellationToken);
        decimal account = await db.BankTransactions.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsPending && t.Currency == "RON" &&
                        (declared == null || t.Account.BankConnectionId == declared) &&
                        (t.BookingDate ?? t.ValueDate) >= start && (t.BookingDate ?? t.ValueDate) <= end)
            .SumAsync(t => t.Amount, cancellationToken);

        RjipView rjip = GetRjipQueryHandler.Build(
            pfaId, start, end,
            entries.Where(e => e.Currency == "RON").Select(e => new RegisterEntry(e, e.Amount, e.DeductibleAmount, null)),
            options.ManualChannelMapping);
        decimal register = rjip.MonthTotals.Sum(t => t.BankIn - t.BankOut);
        decimal difference = account - register;
        return difference == 0
            ? new ReconciliationControlDto(ReconciliationControl.BankBalance, true, true, $"Variația contului {Lei(account)} = RJIP bancă.")
            : new ReconciliationControlDto(ReconciliationControl.BankBalance, false, true, $"Contul {Lei(account)}, RJIP bancă {Lei(register)}: diferență {Lei(difference)}.");
    }

    /// <summary>Payout-urile lunii, cu descompunerea lor (R21) sau fără (R22, R23).</summary>
    private static List<PayoutReconciliationDto> Payouts(List<LedgerEntry> entries) =>
        [.. entries
            .Where(e => e.Source is LedgerSource.Bolt or LedgerSource.Uber && e.BankTransactionId != null)
            .GroupBy(e => e.BankTransactionId!.Value)
            .Select(group =>
            {
                LedgerEntry first = group.OrderByDescending(e => e.Amount).First();
                decimal payout = group.Sum(e => e.Amount);
                if (first.TransactionType == LedgerTransactionType.PlatformSettlement)
                {
                    return new PayoutReconciliationDto(group.Key, first.Date, first.Source, payout, null, null, null, first.ReconciliationStatus);
                }

                decimal gross = group.Where(e => e.TransactionType == LedgerTransactionType.Income).Sum(e => e.Amount);
                decimal commission = group.Where(e => e.TransactionType == LedgerTransactionType.Expense).Sum(e => -e.Amount);
                return new PayoutReconciliationDto(group.Key, first.Date, first.Source, payout, gross, commission, payout - (gross - commission), first.ReconciliationStatus);
            })
            .OrderBy(p => p.Date)];
}
