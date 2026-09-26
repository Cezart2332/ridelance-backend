using System.Globalization;
using System.Text.RegularExpressions;
using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Application.Invoicing;
using Domain.Accounting;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Ledger;

/// <summary>Ce știe un importator despre PFA-ul pe care îl importă.</summary>
/// <param name="From">De la ce dată se importă (începutul colaborării contabile), sau fără limită.</param>
public sealed record LedgerImportContext(
    Guid PfaId,
    Guid UserId,
    DateOnly? From,
    LedgerRules Rules,
    IReadOnlySet<string> ClosedPeriods,
    AccountingOptions Options);

/// <summary>Rezultatul unui importator: înregistrări noi, înregistrări completate, observații.</summary>
public sealed record LedgerImportResult(LedgerSource Source, int Created, int Updated, IReadOnlyList<string> Notes);

/// <summary>
/// O sursă a ledger-ului (spec contabilitate B6). Idempotentă: a doua rulare nu creează nimic nou.
/// Nu salvează; rulatorul salvează după fiecare sursă, ca următoarea să vadă ce a creat aceasta.
/// </summary>
public interface ILedgerSource
{
    /// <summary>Ordinea rulării: banca întâi, apoi platformele și Oblio, care se leagă de ea.</summary>
    int Order { get; }

    Task<LedgerImportResult> ImportAsync(LedgerImportContext context, CancellationToken cancellationToken);
}

/// <summary>Platforma (Bolt, Uber) recunoscută după contrapartida sau detaliile unei plăți.</summary>
internal static class PlatformPayouts
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(200);

    public static LedgerSource? PlatformOf(AccountingOptions options, params string?[] texts)
    {
        string haystack = string.Join(' ', texts.Where(text => !string.IsNullOrWhiteSpace(text)));
        if (haystack.Length == 0)
        {
            return null;
        }

        foreach ((string platform, string pattern) in options.PlatformCounterpartyPatterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern) &&
                Regex.IsMatch(haystack, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, PatternTimeout))
            {
                return platform.ToUpperInvariant() switch
                {
                    "BOLT" => LedgerSource.Bolt,
                    "UBER" => LedgerSource.Uber,
                    _ => null,
                };
            }
        }

        return null;
    }
}

/// <summary>
/// Open Banking: fiecare tranzacție rezervată a conturilor PFA-ului devine o înregistrare.
/// Payout-urile platformelor sunt venit (sau decontare, după <see cref="LedgerIncomeRecognition"/>);
/// plățile se clasifică după <see cref="ExpenseCategoryRule"/>; ce nu se recunoaște intră la verificare.
/// </summary>
internal sealed class BankLedgerSource(IApplicationDbContext db) : ILedgerSource
{
    public int Order => 0;

    public async Task<LedgerImportResult> ImportAsync(LedgerImportContext context, CancellationToken cancellationToken)
    {
        // Conturile PFA-ului: conexiunea declarată în onboarding, altfel toate conturile utilizatorului.
        Guid? connectionId = await db.PfaBankAccountDeclarations
            .Where(d => d.PfaRegistrationId == context.PfaId)
            .Select(d => d.BankConnectionId)
            .FirstOrDefaultAsync(cancellationToken);

        IQueryable<BankTransaction> transactions = db.BankTransactions.AsNoTracking()
            .Where(t => !t.IsPending && (t.BookingDate != null || t.ValueDate != null) && t.Amount != 0);
        transactions = connectionId is { } connection
            ? transactions.Where(t => t.Account.BankConnectionId == connection)
            : transactions.Where(t => t.UserId == context.UserId);
        if (context.From is { } from)
        {
            transactions = transactions.Where(t => (t.BookingDate ?? t.ValueDate) >= from);
        }

        List<BankTransaction> fresh = await transactions
            .Where(t => !db.LedgerEntries.Any(e => e.PfaRegistrationId == context.PfaId && e.BankTransactionId == t.Id))
            .OrderBy(t => t.BookingDate ?? t.ValueDate)
            .ToListAsync(cancellationToken);

        foreach (BankTransaction transaction in fresh)
        {
            LedgerEntry entry = Entry(context, transaction);
            DeductibilityService.Resolve(entry, context.Rules);
            db.LedgerEntries.Add(entry);
        }

        return new LedgerImportResult(LedgerSource.Bank, fresh.Count, 0, []);
    }

    private static LedgerEntry Entry(LedgerImportContext context, BankTransaction transaction)
    {
        DateOnly date = (transaction.BookingDate ?? transaction.ValueDate)!.Value;
        string label = $"Extras {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}";
        string? details = string.IsNullOrWhiteSpace(transaction.RemittanceInfo) ? null : transaction.RemittanceInfo.Trim();
        LedgerSource? platform = PlatformPayouts.PlatformOf(context.Options, transaction.CounterpartyName, details);

        (LedgerSource source, LedgerTransactionType type, string description, string? category, LedgerEntryStatus status) = transaction.Amount switch
        {
            > 0 when platform is { } payout => (
                payout,
                context.Options.IncomeRecognition == LedgerIncomeRecognition.NetPayout ? LedgerTransactionType.Income : LedgerTransactionType.Transfer,
                $"Payout {(payout == LedgerSource.Bolt ? "Bolt" : "Uber")}{(details is null ? string.Empty : $": {details}")}",
                null,
                LedgerEntryStatus.AutoImported),
            > 0 => (
                LedgerSource.Bank,
                LedgerTransactionType.Other,
                $"Încasare neidentificată{(details is null ? string.Empty : $": {details}")}",
                null,
                LedgerEntryStatus.NeedsReview),
            _ => Expense(context, date, transaction.CounterpartyName, details),
        };

        LedgerEntry entry = LedgerSupport.New(
            context.PfaId, date, source, transaction.Id.ToString("N"), label, transaction.CounterpartyName, description,
            type, PaymentMethod.Bank, transaction.Amount, transaction.Currency, status, context.ClosedPeriods);
        entry.BankTransactionId = transaction.Id;
        entry.Category = category;
        return entry;
    }

    private static (LedgerSource, LedgerTransactionType, string, string?, LedgerEntryStatus) Expense(
        LedgerImportContext context, DateOnly date, string? counterparty, string? details)
    {
        ExpenseCategoryRule? rule = DeductibilityService.Classify(context.Rules.Categories, date, counterparty, details);
        return (
            LedgerSource.Bank,
            LedgerTransactionType.Expense,
            details ?? counterparty ?? "Plată bancară",
            rule?.Category,
            rule is null ? LedgerEntryStatus.NeedsReview : LedgerEntryStatus.AutoImported);
    }
}

/// <summary>
/// Uber / Bolt: rapoartele lunare confirmate (B1). Se leagă de payout-urile din bancă ale lunii
/// (sumă netă + dată ± N zile + contrapartidă), ca aceeași încasare să nu fie venit de două ori.
/// Cu <see cref="LedgerIncomeRecognition.GrossReport"/>, raportul aduce venitul brut și comisionul.
/// </summary>
internal sealed class PlatformLedgerSource(IApplicationDbContext db) : ILedgerSource
{
    /// <summary>Diferența acceptată între suma payout-urilor și netul din raport (rotunjiri).</summary>
    private const decimal Tolerance = 0.05m;

    public int Order => 1;

    public async Task<LedgerImportResult> ImportAsync(LedgerImportContext context, CancellationToken cancellationToken)
    {
        var reports = await db.DocumentExtractions
            .AsNoTracking()
            .Where(e => e.IsCurrent &&
                        e.PlatformDocument.PfaRegistrationId == context.PfaId &&
                        e.PlatformDocument.DocumentType == PlatformDocumentType.PlatformReport &&
                        e.PlatformDocument.Platform != null &&
                        (e.PlatformDocument.Status == PlatformDocumentStatus.Confirmed || e.PlatformDocument.Status == PlatformDocumentStatus.Locked))
            .Select(e => new { e.PlatformDocumentId, e.PlatformDocument.Platform, e.PlatformDocument.Period, e.PeriodFrom, e.PeriodTo, e.Amount, e.CommissionAmount, e.Currency })
            .ToListAsync(cancellationToken);

        int created = 0;
        int updated = 0;
        var notes = new List<string>();
        foreach (var report in reports)
        {
            LedgerSource source = report.Platform == Platform.Bolt ? LedgerSource.Bolt : LedgerSource.Uber;
            string name = source == LedgerSource.Bolt ? "Bolt" : "Uber";
            DateOnly from = report.PeriodFrom ?? DateOnly.ParseExact(report.Period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            DateOnly to = report.PeriodTo ?? from.AddMonths(1).AddDays(-1);
            string label = $"Raport {name} {to.ToString("MM.yyyy", CultureInfo.InvariantCulture)}";

            if (context.Options.IncomeRecognition == LedgerIncomeRecognition.GrossReport && report.Amount is { } gross)
            {
                created += await AddOnceAsync(context, report.PlatformDocumentId, "income", to, source, label,
                    $"Venit brut din curse, {name}", LedgerTransactionType.Income, gross, report.Currency, cancellationToken);
                if (report.CommissionAmount is { } commission)
                {
                    created += await AddOnceAsync(context, report.PlatformDocumentId, "commission", to, source, label,
                        $"Comision reținut de {name}", LedgerTransactionType.Expense, -Math.Abs(commission), report.Currency, cancellationToken);
                }
            }

            if (report.Amount is not { } income || await db.LedgerEntries.AnyAsync(e => e.PlatformDocumentId == report.PlatformDocumentId && e.BankTransactionId != null, cancellationToken))
            {
                continue;
            }

            decimal net = income - (report.CommissionAmount ?? 0);
            DateOnly until = to.AddDays(context.Options.PayoutMatchDays);
            List<LedgerEntry> payouts = await db.LedgerEntries
                .Where(e => e.PfaRegistrationId == context.PfaId &&
                            e.Source == source &&
                            e.BankTransactionId != null &&
                            e.PlatformDocumentId == null &&
                            e.Amount > 0 &&
                            e.Date >= from && e.Date <= until)
                .ToListAsync(cancellationToken);

            if (payouts.Count > 0 && Math.Abs(payouts.Sum(e => e.Amount) - net) <= Tolerance)
            {
                payouts.ForEach(e => e.PlatformDocumentId = report.PlatformDocumentId);
                updated += payouts.Count;
            }
            else
            {
                notes.Add($"{label}: payout-urile din bancă ({AccountingJson.Amount(payouts.Sum(e => e.Amount))} lei) nu dau netul din raport ({AccountingJson.Amount(net)} lei).");
            }
        }

        return new LedgerImportResult(LedgerSource.Bolt, created, updated, notes);
    }

    private async Task<int> AddOnceAsync(
        LedgerImportContext context,
        Guid documentId,
        string part,
        DateOnly date,
        LedgerSource source,
        string label,
        string description,
        LedgerTransactionType type,
        decimal amount,
        string? currency,
        CancellationToken cancellationToken)
    {
        string externalId = $"{documentId:N}:{part}";
        if (await db.LedgerEntries.AnyAsync(e => e.Source == source && e.ExternalId == externalId, cancellationToken))
        {
            return 0;
        }

        LedgerEntry entry = LedgerSupport.New(
            context.PfaId, date, source, externalId, label, source == LedgerSource.Bolt ? "Bolt" : "Uber", description,
            type, PaymentMethod.Bank, amount, currency ?? "RON", LedgerEntryStatus.AutoImported, context.ClosedPeriods);
        entry.PlatformDocumentId = documentId;
        db.LedgerEntries.Add(entry);
        return 1;
    }
}

/// <summary>
/// Oblio: facturile emise de PFA și încasate. Încasarea se caută întâi în bancă (aceeași sumă, după
/// emitere): intrarea bancară devine venitul facturii. Fără plată bancară, factura intră ca
/// încasare de verificat — metoda de plată nu se ghicește.
/// </summary>
internal sealed class OblioLedgerSource(IApplicationDbContext db, OwnerOblioResolver resolver, IOwnerInvoicingService invoicing) : ILedgerSource
{
    public int Order => 2;

    public async Task<LedgerImportResult> ImportAsync(LedgerImportContext context, CancellationToken cancellationToken)
    {
        Result<OwnerOblioCredentials> credentials = await resolver.ResolveAsync(context.UserId, cancellationToken);
        if (credentials.IsFailure)
        {
            return new LedgerImportResult(LedgerSource.Oblio, 0, 0, []);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        IReadOnlyList<OwnerInvoice> invoices;
        try
        {
            invoices = await invoicing.ListInvoicesAsync(credentials.Value, context.From ?? today.AddYears(-1), today, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new LedgerImportResult(LedgerSource.Oblio, 0, 0, ["Oblio nu a răspuns; facturile se importă la rularea următoare."]);
        }

        int created = 0;
        int updated = 0;
        foreach (OwnerInvoice invoice in invoices.Where(i => !i.Canceled && i.CollectedLei > 0))
        {
            string label = LedgerSupport.Cut($"Factura {invoice.SeriesName} {invoice.Number}", LedgerSupport.DocumentLabelLength);
            string externalId = $"{context.PfaId:N}:{invoice.SeriesName}-{invoice.Number}";
            bool known = await db.LedgerEntries.AnyAsync(
                e => e.PfaRegistrationId == context.PfaId && (e.Source == LedgerSource.Oblio && e.ExternalId == externalId || e.DocumentLabel == label),
                cancellationToken);
            if (known)
            {
                continue;
            }

            DateOnly until = invoice.IssueDate.AddDays(context.Options.InvoiceMatchDays);
            LedgerEntry? payment = await db.LedgerEntries
                .Where(e => e.PfaRegistrationId == context.PfaId &&
                            e.Source == LedgerSource.Bank &&
                            e.BankTransactionId != null &&
                            e.Amount >= invoice.CollectedLei - 0.01m && e.Amount <= invoice.CollectedLei + 0.01m &&
                            e.Date >= invoice.IssueDate && e.Date <= until &&
                            (e.TransactionType == LedgerTransactionType.Other || e.TransactionType == LedgerTransactionType.Income) &&
                            !e.DocumentLabel.StartsWith("Factura ") &&
                            (e.Status == LedgerEntryStatus.AutoImported || e.Status == LedgerEntryStatus.NeedsReview) &&
                            !e.ClosedPeriodFlag)
                .OrderBy(e => e.Date)
                .FirstOrDefaultAsync(cancellationToken);

            if (payment is not null)
            {
                payment.TransactionType = LedgerTransactionType.Income;
                payment.DocumentLabel = label;
                payment.Counterparty ??= LedgerSupport.Cut(invoice.ClientName, LedgerSupport.CounterpartyLength);
                payment.Description = LedgerSupport.Cut($"Încasare {label}, {invoice.ClientName}", LedgerSupport.DescriptionLength);
                payment.Status = LedgerEntryStatus.AutoImported;
                updated++;
                continue;
            }

            db.LedgerEntries.Add(LedgerSupport.New(
                context.PfaId, invoice.IssueDate, LedgerSource.Oblio, externalId, label, invoice.ClientName,
                $"Încasare {label} fără plată bancară găsită: verifică metoda de plată.",
                LedgerTransactionType.Income, PaymentMethod.Cash, invoice.CollectedLei, "RON", LedgerEntryStatus.NeedsReview, context.ClosedPeriods));
            created++;
        }

        return new LedgerImportResult(LedgerSource.Oblio, created, updated, []);
    }
}
