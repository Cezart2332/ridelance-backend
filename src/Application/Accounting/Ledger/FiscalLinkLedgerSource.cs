using System.Text;
using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Application.Accounting.Declarations;
using Domain.Accounting;
using Domain.Documents;
using Domain.FiscalLink;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Ledger;

/// <summary>Cash-only Z income; receipts justify the Z and never create a second income.</summary>
internal sealed class FiscalLinkLedgerSource(
    IApplicationDbContext db,
    IFiscalLinkAccountingService fiscalLink,
    DeclarationFiles files) : ILedgerSource
{
    public int Order => 4;

    public async Task<LedgerImportResult> ImportAsync(LedgerImportContext context, CancellationToken cancellationToken)
    {
        FiscalLinkClient? client = await db.FiscalLinkClients.SingleOrDefaultAsync(
            c => c.PfaRegistrationId == context.PfaId && c.UserId == context.UserId, cancellationToken);
        if (client is null)
        {
            return new LedgerImportResult(LedgerSource.CashZ, 0, 0, []);
        }

        client.LastSyncAttemptAtUtc = DateTime.UtcNow;
        Result<FiscalLinkCashDocuments> fetched = await fiscalLink.ReadAsync(client.FiscalLinkClientId, cancellationToken);
        if (fetched.IsFailure)
        {
            client.LastSyncError = LedgerSupport.Cut(fetched.Error.Description, 1000);
            return new LedgerImportResult(LedgerSource.CashZ, 0, 0, [client.LastSyncError]);
        }

        var notes = fetched.Value.Notes.ToList();
        TimeZoneInfo romania = PfaDashboard.PfaDashboardPeriod.RomaniaTimeZone();
        DateOnly Day(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), romania));
        bool Included(DateTime utc) => (context.From is null || Day(utc) >= context.From) && (context.To is null || Day(utc) <= context.To);

        FiscalReceiptInput[] receiptInputs = [.. fetched.Value.Receipts.Where(r => Included(r.IssuedAtUtc))
            .Select(r => new FiscalReceiptInput(r.Serial, r.Number, r.IssuedAtUtc, r.Total, r.ExternalId))];
        // Receipt ingestion is shared with other transports; successful retries remain idempotent.
        Result<int> recorded = await new RecordFiscalReceiptsCommandHandler(db)
            .Handle(new RecordFiscalReceiptsCommand(context.PfaId, receiptInputs), cancellationToken);
        if (recorded.IsFailure)
        {
            client.LastSyncError = recorded.Error.Description;
            return new LedgerImportResult(LedgerSource.CashZ, 0, 0, [recorded.Error.Description]);
        }
        int created = 0;
        int updated = 0;
        foreach (FiscalLinkCashZ input in fetched.Value.Reports.Where(r => Included(r.IssuedAtUtc)).OrderBy(r => r.IssuedAtUtc))
        {
            DateOnly date = Day(input.IssuedAtUtc);
            string serial = input.Serial.Trim().ToUpperInvariant();
            string number = input.Number.Trim();
            if (serial.Length is 0 or > 32 || number.Length is 0 or > 32 || input.Total < 0)
            {
                notes.Add("Un raport Z FiscalLink are date invalide și nu a fost importat.");
                continue;
            }

            List<ZReport> candidates = await db.ZReports.Where(z => z.PfaRegistrationId == context.PfaId && z.Date == date && z.ZNumber == number)
                .ToListAsync(cancellationToken);
            candidates.AddRange(db.ZReports.Local.Where(z => z.PfaRegistrationId == context.PfaId && z.Date == date && z.ZNumber == number &&
                candidates.All(existing => existing.Id != z.Id)));
            ZReport? existing = candidates.Find(z => z.RegisterSerial == serial) ??
                (candidates.Count == 1 && string.IsNullOrWhiteSpace(candidates[0].RegisterSerial) ? candidates[0] : null);
            if (existing is not null)
            {
                LedgerEntry? entry = existing.LedgerEntryId is { } entryId
                    ? await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken) : null;
                if (existing.Total != LedgerInvariants.Round(input.Total))
                {
                    notes.Add($"Casa {serial}, Z {number} din {date:dd.MM.yyyy}: totalul FiscalLink diferă de cel înregistrat; verifică documentul.");
                    if (entry is not null && entry.Status != LedgerEntryStatus.Locked && !context.ClosedPeriods.Contains(entry.AccountingPeriod))
                    {
                        entry.ReconciliationStatus = ReconciliationStatus.NeedsReview;
                    }
                    continue;
                }

                // Keep accountant-verified/closed history intact. A matching manual Z is reused.
                if (context.ClosedPeriods.Contains(LedgerSupport.PeriodOf(date)) || entry?.Status == LedgerEntryStatus.Locked)
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(existing.RegisterSerial))
                {
                    existing.RegisterSerial = serial;
                    existing.TotalCash = existing.Total;
                    existing.TotalCard = 0;
                    updated++;
                }
                await ZControls.CheckAsync(db, existing, cancellationToken);
                if (entry?.ReconciliationStatus == ReconciliationStatus.NeedsReview)
                {
                    notes.Add($"Casa {serial}, Z {number}: suma bonurilor diferă de raportul Z; verifică documentele.");
                }
                continue;
            }

            Document document = await files.StoreAsync(context.PfaId, Encoding.UTF8.GetBytes(input.Json),
                $"FiscalLink_Z_{number}_{date:yyyyMMdd}.json", "application/json", cancellationToken, DocumentOrigin.AccountingGenerated);
            LedgerEntry? income = null;
            if (input.Total > 0)
            {
                income = LedgerSupport.New(context.PfaId, date, LedgerSource.CashZ,
                    $"{context.PfaId:N}:{serial}:{date:yyyyMMdd}:Z{number}", $"Raport Z nr. {number}", null,
                    $"Încasări numerar FiscalLink, casa {serial}, raport Z nr. {number}", LedgerTransactionType.Income,
                    PaymentMethod.Cash, LedgerInvariants.Round(input.Total), "RON", LedgerEntryStatus.AutoImported, context.ClosedPeriods);
                income.SourceDocumentId = document.Id;
                income.ReconciliationStatus = ReconciliationStatus.Matched;
                db.LedgerEntries.Add(income);
                created++;
            }
            var report = new ZReport
            {
                Id = Guid.NewGuid(), PfaRegistrationId = context.PfaId, Date = date, ZNumber = number,
                RegisterSerial = serial, Total = LedgerInvariants.Round(input.Total), TotalCash = LedgerInvariants.Round(input.Total),
                TotalCard = 0, DocumentId = document.Id, LedgerEntryId = income?.Id,
            };
            db.ZReports.Add(report);
            await ZControls.CheckAsync(db, report, cancellationToken);
            if (income?.ReconciliationStatus == ReconciliationStatus.NeedsReview)
            {
                notes.Add($"Casa {serial}, Z {number}: suma bonurilor diferă de raportul Z; verifică documentele.");
            }
            AccountingAudit.Record(db, context.PfaId, nameof(ZReport), report.Id, "FISCALLINK_IMPORT", null,
                new { serial, number, date, input.Total, input.ExternalId }, null, null);
            if (income?.ClosedPeriodFlag == true)
            {
                notes.Add($"Z {number} din {date:dd.MM.yyyy}: luna este închisă; încasarea așteaptă corecția contabilului.");
            }
        }

        client.LastSyncError = notes.Count == 0 ? null : LedgerSupport.Cut(string.Join(" ", notes), 1000);
        if (fetched.Value.Notes.Count == 0)
        {
            client.LastSyncAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        return new LedgerImportResult(LedgerSource.CashZ, created, updated,
            [$"FiscalLink: {recorded.Value} bonuri noi, {created} încasări Z noi.", .. notes]);
    }
}
