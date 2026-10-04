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
/// <param name="To">Data încetării colaborării: operațiunile de după ea se ignoră (B8).</param>
public sealed record LedgerImportContext(
    Guid PfaId,
    Guid UserId,
    DateOnly? From,
    LedgerRules Rules,
    IReadOnlySet<string> ClosedPeriods,
    AccountingOptions Options,
    DateOnly? To = null);

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
/// Open Banking: fiecare tranzacție rezervată a conturilor PFA-ului devine o înregistrare. Întâi
/// contrapartida (spec flux contabil §6): payout de platformă (R20, <c>NEEDS_RECONCILIATION</c>, nu venit),
/// titularul (R40, R41), ANAF/Trezoreria (R42), alt cont al PFA-ului (R43). Restul: plățile se
/// clasifică după <see cref="ExpenseCategoryRule"/> și rămân fără document (R01), încasările
/// neidentificate intră la verificare.
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
            .Where(t => !t.IsPending && (t.BookingDate != null || t.ValueDate != null) && t.Amount != 0 &&
                t.UserId == context.UserId && t.Account.UserId == context.UserId && t.Account.IsActive &&
                t.Account.Connection.Status == BankConnectionStatus.Linked &&
                t.ProviderConsentId == t.Account.Connection.ProviderConsentId);
        transactions = connectionId is { } connection
            ? transactions.Where(t => t.Account.BankConnectionId == connection)
            : transactions.Where(t => t.UserId == context.UserId);
        if (context.From is { } from)
        {
            transactions = transactions.Where(t => (t.BookingDate ?? t.ValueDate) >= from);
        }

        if (context.To is { } to)
        {
            transactions = transactions.Where(t => (t.BookingDate ?? t.ValueDate) <= to);
        }

        // O tranzacție cu propunerea de asociere în așteptare nu devine încă înregistrare (R36).
        List<BankTransaction> fresh = await transactions
            .Where(t => !db.LedgerEntries.Any(e => e.PfaRegistrationId == context.PfaId && e.BankTransactionId == t.Id) &&
                        !db.LedgerMatchProposals.Any(p => p.PfaRegistrationId == context.PfaId && p.BankTransactionId == t.Id && p.Accepted != false))
            .OrderBy(t => t.BookingDate ?? t.ValueDate)
            .ToListAsync(cancellationToken);
        PfaIdentity identity = await IdentityAsync(context, cancellationToken);
        List<CounterpartyClassificationRule> learned = await db.CounterpartyClassificationRules.AsNoTracking()
            .Where(r => r.PfaRegistrationId == context.PfaId)
            .ToListAsync(cancellationToken);
        int updated = await ProposeForOpenAsync(context, identity, learned, cancellationToken);
        if (fresh.Count == 0)
        {
            return new LedgerImportResult(LedgerSource.Bank, 0, updated, []);
        }

        List<Guid> rejected = await db.LedgerMatchProposals
            .Where(p => p.PfaRegistrationId == context.PfaId && p.Accepted == false)
            .Select(p => p.BankTransactionId)
            .ToListAsync(cancellationToken);
        Dictionary<BankTransaction, LedgerEntry> proposals = await ReceiptPaymentsAsync(context, identity, fresh, rejected, cancellationToken);
        foreach ((BankTransaction transaction, LedgerEntry receipt) in proposals)
        {
            db.LedgerMatchProposals.Add(new LedgerMatchProposal
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = context.PfaId,
                BankTransactionId = transaction.Id,
                LedgerEntryId = receipt.Id,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        int created = 0;
        foreach (BankTransaction transaction in fresh.Where(t => !proposals.ContainsKey(t)))
        {
            LedgerEntry entry = Entry(context, identity, transaction, learned);
            DeductibilityService.Resolve(entry, context.Rules);
            db.LedgerEntries.Add(entry);
            created++;
        }

        IReadOnlyList<string> notes = proposals.Count == 0
            ? []
            : [$"{proposals.Count} plăți din bancă par să fie ale unor bonuri deja înregistrate; confirmă asocierea."];
        return new LedgerImportResult(LedgerSource.Bank, created, updated, notes);
    }

    /// <summary>
    /// Excepțiile deja importate din lunile deschise, încă fără propunere: primesc regula învățată sau
    /// propunerea din contrapartidă (aceleași reguli ca la import). Lunile închise nu se ating.
    /// </summary>
    private async Task<int> ProposeForOpenAsync(
        LedgerImportContext context, PfaIdentity identity, IReadOnlyList<CounterpartyClassificationRule> learned, CancellationToken cancellationToken)
    {
        List<string> closed = [.. context.ClosedPeriods];
        List<LedgerEntry> open = await db.LedgerEntries
            .Where(e => e.PfaRegistrationId == context.PfaId && e.Source == LedgerSource.Bank && e.BankTransactionId != null &&
                        e.StornoOfEntryId == null && !e.ClosedPeriodFlag && e.Status != LedgerEntryStatus.Locked && !closed.Contains(e.AccountingPeriod) &&
                        e.ProposedClassification == null && e.SourceDocumentId == null && e.EFacturaMessageId == null &&
                        (e.ReconciliationStatus == ReconciliationStatus.NeedsReview ||
                         e.ReconciliationStatus == ReconciliationStatus.Unmatched && e.Category == null))
            .ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return 0;
        }

        List<Guid> ids = [.. open.Select(e => e.BankTransactionId!.Value)];
        Dictionary<Guid, BankTransaction> transactions = await db.BankTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        int updated = 0;
        foreach (LedgerEntry entry in open)
        {
            if (!transactions.TryGetValue(entry.BankTransactionId!.Value, out BankTransaction? transaction))
            {
                continue;
            }

            string? details = string.IsNullOrWhiteSpace(transaction.RemittanceInfo) ? null : transaction.RemittanceInfo.Trim();
            if (BankClassifications.Match(learned, transaction.Amount, transaction.CounterpartyName, transaction.CounterpartyIban, details) is { } rule)
            {
                BankClassifications.Apply(entry, rule.Classification, context.Rules, verified: false);
                updated++;
                continue;
            }

            CounterpartyKind kind = CounterpartyRules.Classify(
                transaction.Amount, transaction.CounterpartyName, transaction.CounterpartyIban, details, identity, context.Options);
            if (kind != CounterpartyKind.PlatformSettlement && BankClassifications.FromKind(kind) is { } proposal)
            {
                BankClassifications.Propose(entry, proposal);
                updated++;
            }
        }

        return updated;
    }

    /// <summary>
    /// R36: bonurile plătite cu card neconectat (canal <c>MANUAL</c>, fără tranzacție) și plățile noi din bancă
    /// cu aceeași sumă, același comerciant și data în ± N zile. Doar perechile unice, în ambele sensuri.
    /// </summary>
    private async Task<Dictionary<BankTransaction, LedgerEntry>> ReceiptPaymentsAsync(
        LedgerImportContext context, PfaIdentity identity, List<BankTransaction> fresh, List<Guid> rejected, CancellationToken cancellationToken)
    {
        List<LedgerEntry> receipts = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.PfaRegistrationId == context.PfaId &&
                        e.PaymentMethod == PaymentMethod.Manual &&
                        e.BankTransactionId == null &&
                        e.Amount < 0 &&
                        e.Status != LedgerEntryStatus.Locked &&
                        !db.LedgerMatchProposals.Any(p => p.LedgerEntryId == e.Id && p.Accepted != false))
            .ToListAsync(cancellationToken);
        if (receipts.Count == 0)
        {
            return [];
        }

        int days = context.Options.ExpenseMatchDays;
        List<(BankTransaction Transaction, LedgerEntry Receipt)> pairs = [.. fresh
            .Where(t => t.Amount < 0 && !rejected.Contains(t.Id) &&
                        CounterpartyRules.Classify(t.Amount, t.CounterpartyName, t.CounterpartyIban, t.RemittanceInfo, identity, context.Options) == CounterpartyKind.None)
            .SelectMany(t => receipts
                .Where(r => r.Amount == t.Amount &&
                            Math.Abs(r.Date.DayNumber - (t.BookingDate ?? t.ValueDate)!.Value.DayNumber) <= days &&
                            (CounterpartyRules.SimilarMerchant(r.Counterparty, t.CounterpartyName) ||
                             CounterpartyRules.SimilarMerchant(r.Counterparty, t.RemittanceInfo)))
                .Select(r => (t, r)))];

        return pairs
            .Where(pair => pairs.Count(p => p.Transaction == pair.Transaction) == 1 && pairs.Count(p => p.Receipt.Id == pair.Receipt.Id) == 1)
            .ToDictionary(pair => pair.Transaction, pair => pair.Receipt);
    }

    /// <summary>Numele titularului și conturile proprii, pentru R40–R43.</summary>
    private async Task<PfaIdentity> IdentityAsync(LedgerImportContext context, CancellationToken cancellationToken)
    {
        var pfa = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.Id == context.PfaId)
            .Select(p => new { p.HolderName, p.FullName, p.User.FirstName, p.User.LastName })
            .SingleOrDefaultAsync(cancellationToken);
        List<string> names = [.. new[] { pfa?.HolderName, pfa?.FullName, $"{pfa?.FirstName} {pfa?.LastName}" }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)];

        List<string?> ibans = await db.BankAccounts.AsNoTracking()
            .Where(a => a.UserId == context.UserId && a.Iban != null)
            .Select(a => a.Iban)
            .ToListAsync(cancellationToken);
        return new PfaIdentity(names, ibans.Where(i => i is not null).Select(i => i!.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// Înregistrarea unei tranzacții (spec flux contabil §6): payout-ul platformei, transferul între
    /// conturile proprii și plata la un IBAN de Trezorerie sunt sigure; o regulă învățată pe contrapartidă
    /// se aplică direct; titularul, ANAF după nume și comisionul bancar rămân propuneri de confirmat.
    /// </summary>
    private static LedgerEntry Entry(
        LedgerImportContext context, PfaIdentity identity, BankTransaction transaction, IReadOnlyList<CounterpartyClassificationRule> learned)
    {
        DateOnly date = (transaction.BookingDate ?? transaction.ValueDate)!.Value;
        string label = $"Extras {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}";
        string? details = string.IsNullOrWhiteSpace(transaction.RemittanceInfo) ? null : transaction.RemittanceInfo.Trim();
        string suffix = details is null ? string.Empty : $": {details}";
        CounterpartyKind kind = CounterpartyRules.Classify(
            transaction.Amount, transaction.CounterpartyName, transaction.CounterpartyIban, details, identity, context.Options);

        if (kind == CounterpartyKind.PlatformSettlement)
        {
            LedgerSource platform = PlatformPayouts.PlatformOf(context.Options, transaction.CounterpartyName, details)!.Value;
            LedgerEntry payout = LedgerSupport.New(
                context.PfaId, date, platform, transaction.Id.ToString("N"), label, transaction.CounterpartyName,
                $"Payout {(platform == LedgerSource.Bolt ? "Bolt" : "Uber")}{suffix}",
                LedgerTransactionType.PlatformSettlement, PaymentMethod.Bank, transaction.Amount, transaction.Currency, LedgerEntryStatus.AutoImported, context.ClosedPeriods);
            payout.BankTransactionId = transaction.Id;
            payout.ReconciliationStatus = ReconciliationStatus.NeedsReconciliation;
            return payout;
        }

        LedgerEntry entry = LedgerSupport.New(
            context.PfaId, date, LedgerSource.Bank, transaction.Id.ToString("N"), label, transaction.CounterpartyName,
            (details ?? transaction.CounterpartyName ?? (transaction.Amount > 0 ? "Încasare bancară" : "Plată bancară")).Trim(),
            transaction.Amount > 0 ? LedgerTransactionType.Other : LedgerTransactionType.Expense,
            PaymentMethod.Bank, transaction.Amount, transaction.Currency, LedgerEntryStatus.AutoImported, context.ClosedPeriods);
        entry.BankTransactionId = transaction.Id;

        bool certain = kind == CounterpartyKind.InternalTransfer ||
                       kind == CounterpartyKind.Tax && CounterpartyRules.IsTaxAuthority(transaction.CounterpartyName, transaction.CounterpartyIban, context.Options);
        BankClassification? proposal = BankClassifications.FromKind(kind);
        if (certain)
        {
            BankClassifications.Apply(entry, proposal!.Value, context.Rules, verified: false);
        }
        else if (BankClassifications.Match(learned, transaction.Amount, transaction.CounterpartyName, transaction.CounterpartyIban, details) is { } rule)
        {
            BankClassifications.Apply(entry, rule.Classification, context.Rules, verified: false);
        }
        else if (proposal is { } proposed)
        {
            BankClassifications.Propose(entry, proposed);
        }
        else if (transaction.Amount > 0)
        {
            // Încasare neidentificată: în RJIP (mișcare efectivă), dar nu venit până la clasificare.
            entry.ReconciliationStatus = ReconciliationStatus.NeedsReview;
            if (!entry.ClosedPeriodFlag)
            {
                entry.Status = LedgerEntryStatus.NeedsReview;
            }
        }
        else
        {
            ExpenseCategoryRule? category = DeductibilityService.Classify(context.Rules.Categories, date, transaction.CounterpartyName, details);
            entry.Category = category?.Category;
            entry.ReconciliationStatus = ReconciliationStatus.Unmatched;
            if (category is null && !entry.ClosedPeriodFlag)
            {
                entry.Status = LedgerEntryStatus.NeedsReview;
            }
        }

        entry.Description = LedgerSupport.Cut(entry.Description, LedgerSupport.DescriptionLength);
        return entry;
    }
}

/// <summary>
/// Uber / Bolt: reconcilierea payout-urilor cu raportul lunar (spec flux contabil R21–R23).
/// </summary>
/// <remarks>
/// <para>
/// Cu raportul confirmat (venit online și comision) și factura de comision a lunii, payout-urile din
/// perioadă (până la N zile după ea) care dau exact netul raportului se descompun: fiecare payout devine
/// venitul brut + comisionul lui, la data decontării, legate de aceeași tranzacție bancară și de același
/// <see cref="LedgerEntry.SettlementGroupId"/>. La mai multe payout-uri, comisionul se împarte
/// proporțional, iar brutul fiecăruia e payout + comision.
/// </para>
/// <para>
/// Fără documente suficiente payout-ul rămâne <c>NEEDS_RECONCILIATION</c> (R22); cu o diferență de
/// sumă, <c>NEEDS_REVIEW</c>, cu diferența în note, fără nicio înregistrare automată (R23).
/// </para>
/// </remarks>
internal sealed class PlatformLedgerSource(IApplicationDbContext db) : ILedgerSource
{
    public int Order => 1;

    public async Task<LedgerImportResult> ImportAsync(LedgerImportContext context, CancellationToken cancellationToken)
    {
        var reports = await db.DocumentExtractions
            .AsNoTracking()
            .Where(e => e.IsCurrent &&
                        e.PlatformDocument.PfaRegistrationId == context.PfaId &&
                        e.PlatformDocument.DeletedAtUtc == null &&
                        e.PlatformDocument.DocumentType == PlatformDocumentType.PlatformReport &&
                        e.PlatformDocument.Platform != null &&
                        (e.PlatformDocument.Status == PlatformDocumentStatus.Confirmed || e.PlatformDocument.Status == PlatformDocumentStatus.Locked))
            .Select(e => new { e.PlatformDocumentId, e.PlatformDocument.Platform, e.PlatformDocument.Period, e.PeriodFrom, e.PeriodTo, e.Amount, e.CommissionAmount, e.Currency })
            .ToListAsync(cancellationToken);

        int updated = 0;
        int created = 0;
        var notes = new List<string>();
        foreach (var report in reports.OrderBy(r => r.Period, StringComparer.Ordinal))
        {
            LedgerSource source = report.Platform == Platform.Bolt ? LedgerSource.Bolt : LedgerSource.Uber;
            string name = source == LedgerSource.Bolt ? "Bolt" : "Uber";
            DateOnly from = report.PeriodFrom ?? DateOnly.ParseExact(report.Period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            DateOnly to = report.PeriodTo ?? from.AddMonths(1).AddDays(-1);
            if (context.To is { } end && from > end)
            {
                continue;
            }

            string month = to.ToString("MM.yyyy", CultureInfo.InvariantCulture);
            if (await db.LedgerEntries.AnyAsync(e => e.PlatformDocumentId == report.PlatformDocumentId && e.SettlementGroupId != null, cancellationToken))
            {
                continue;
            }

            DateOnly until = to.AddDays(context.Options.PayoutMatchDays);
            List<LedgerEntry> payouts = await db.LedgerEntries
                .Where(e => e.PfaRegistrationId == context.PfaId &&
                            e.Source == source &&
                            e.TransactionType == LedgerTransactionType.PlatformSettlement &&
                            e.BankTransactionId != null &&
                            e.Status != LedgerEntryStatus.Locked &&
                            !e.ClosedPeriodFlag &&
                            e.Date >= from && e.Date <= until)
                .OrderBy(e => e.Date)
                .ThenBy(e => e.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            // R22: fără venitul online, comision și factura de comision nu se descompune nimic.
            bool invoice = await db.PlatformDocuments.AnyAsync(
                d => d.PfaRegistrationId == context.PfaId && d.Platform == report.Platform && d.Period == report.Period &&
                     d.DeletedAtUtc == null && d.DocumentType == PlatformDocumentType.CommissionInvoice &&
                     (d.Status == PlatformDocumentStatus.Confirmed || d.Status == PlatformDocumentStatus.Locked),
                cancellationToken);
            if (report.Amount is not { } gross || report.CommissionAmount is not { } rawCommission || !invoice)
            {
                notes.Add($"{name} {month}: raportul sau factura de comision lipsesc; payout-urile rămân nereconciliate.");
                continue;
            }

            decimal commission = Math.Abs(rawCommission);
            decimal net = gross - commission;
            decimal paid = payouts.Sum(e => e.Amount);
            if (payouts.Count == 0 || net <= 0 || !string.Equals(report.Currency ?? "RON", "RON", StringComparison.OrdinalIgnoreCase) || paid != net)
            {
                // R23: diferența se arată, nu se înregistrează.
                payouts.Where(e => e.ReconciliationStatus == ReconciliationStatus.NeedsReconciliation)
                    .ToList()
                    .ForEach(e => e.ReconciliationStatus = ReconciliationStatus.NeedsReview);
                notes.Add($"{name} {month}: payout-urile din bancă ({AccountingJson.Amount(paid)} lei) nu dau netul din raport ({AccountingJson.Amount(net)} lei), diferență {AccountingJson.Amount(paid - net)} lei.");
                continue;
            }

            decimal allocated = 0;
            for (int i = 0; i < payouts.Count; i++)
            {
                LedgerEntry payout = payouts[i];
                decimal share = i == payouts.Count - 1
                    ? commission - allocated
                    : LedgerInvariants.Round(commission * payout.Amount / net);
                allocated += share;
                db.LedgerEntries.Add(Settle(context, payout, report.PlatformDocumentId, name, month, share));
                created++;
                updated++;
            }
        }

        return new LedgerImportResult(LedgerSource.Bolt, created, updated, notes);
    }

    /// <summary>
    /// Payout-ul devine venitul brut (payout + comision), iar comisionul o plată separată, pe aceeași
    /// tranzacție și în același grup: împreună dau exact suma virată.
    /// </summary>
    private static LedgerEntry Settle(LedgerImportContext context, LedgerEntry payout, Guid reportId, string name, string month, decimal commission)
    {
        var group = Guid.NewGuid();
        payout.Amount = LedgerInvariants.Round(payout.Amount + commission);
        payout.TransactionType = LedgerTransactionType.Income;
        payout.Description = LedgerSupport.Cut($"Venit brut din curse {name}, raport {month}", LedgerSupport.DescriptionLength);
        payout.PlatformDocumentId = reportId;
        payout.SettlementGroupId = group;
        payout.ReconciliationStatus = ReconciliationStatus.Matched;

        LedgerEntry fee = LedgerSupport.New(
            context.PfaId, payout.Date, payout.Source, $"{payout.BankTransactionId:N}:commission", payout.DocumentLabel, name,
            $"Comision {name} reținut din payout, raport {month}", LedgerTransactionType.Expense, PaymentMethod.Bank,
            -commission, payout.Currency, LedgerEntryStatus.AutoImported, context.ClosedPeriods);
        fee.BankTransactionId = payout.BankTransactionId;
        fee.PlatformDocumentId = reportId;
        fee.SettlementGroupId = group;
        fee.ReconciliationStatus = ReconciliationStatus.Matched;
        fee.Category = LedgerSupport.PlatformCommissionCategory;
        DeductibilityService.Resolve(fee, context.Rules);
        return fee;
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
            DateOnly until = context.To is { } end && end < today ? end : today;
            invoices = await invoicing.ListInvoicesAsync(credentials.Value, context.From ?? today.AddYears(-1), until, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new LedgerImportResult(LedgerSource.Oblio, 0, 0, ["Oblio nu a răspuns; facturile se importă la rularea următoare."]);
        }

        int created = 0;
        int updated = 0;
        foreach (OwnerInvoice invoice in invoices.Where(i => !i.Canceled && i.CollectedLei > 0 && (context.To is not { } last || i.IssueDate <= last)))
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
