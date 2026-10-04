using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Ledger;

/// <summary>Un bon emis de casa de marcat, cum vine de la FiscalLink sau din alt import.</summary>
public sealed record FiscalReceiptInput(string RegisterSerial, string Number, DateTime IssuedAtUtc, decimal Total, string? ExternalId = null);

/// <summary>
/// Bonurile casei de marcat (spec flux contabil R11): se păstrează, nu creează încasări. Încasarea
/// numerar a zilei e raportul Z (R10), iar bonurile îl verifică (R12). Idempotent pe serie + număr + dată.
/// </summary>
public sealed record RecordFiscalReceiptsCommand(Guid PfaId, IReadOnlyList<FiscalReceiptInput> Receipts) : ICommand<int>;

internal sealed class RecordFiscalReceiptsCommandHandler(IApplicationDbContext db) : ICommandHandler<RecordFiscalReceiptsCommand, int>
{
    public async Task<Result<int>> Handle(RecordFiscalReceiptsCommand command, CancellationToken cancellationToken)
    {
        if (command.Receipts.Any(r => string.IsNullOrWhiteSpace(r.RegisterSerial) || r.RegisterSerial.Trim().Length > 32 ||
                                      string.IsNullOrWhiteSpace(r.Number) || r.Number.Trim().Length > 32 || r.Total < 0 ||
                                      r.ExternalId?.Length > 64))
        {
            return Result.Failure<int>(Error.Problem("Accounting.InvalidFiscalReceipt", "Bonul fiscal are date invalide (serie, număr sau total)."));
        }
        TimeZoneInfo romania = PfaDashboard.PfaDashboardPeriod.RomaniaTimeZone();
        var known = (await db.FiscalReceipts.AsNoTracking()
                .Where(r => r.PfaRegistrationId == command.PfaId)
                .Select(r => new { r.RegisterSerial, r.Number, r.Date })
                .ToListAsync(cancellationToken))
            .Select(r => (r.RegisterSerial, r.Number, r.Date))
            .ToHashSet();

        var dates = new HashSet<DateOnly>();
        int added = 0;
        foreach (FiscalReceiptInput input in command.Receipts)
        {
            var utc = DateTime.SpecifyKind(input.IssuedAtUtc, DateTimeKind.Utc);
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, romania));
            string serial = input.RegisterSerial.Trim().ToUpperInvariant();
            string number = input.Number.Trim();
            if (!known.Add((serial, number, date)))
            {
                continue;
            }

            db.FiscalReceipts.Add(new FiscalReceipt
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = command.PfaId,
                RegisterSerial = serial,
                Number = number,
                Date = date,
                IssuedAtUtc = utc,
                Total = LedgerInvariants.Round(input.Total),
                ExternalId = input.ExternalId,
                CreatedAtUtc = DateTime.UtcNow,
            });
            dates.Add(date);
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);

        List<ZReport> reports = await db.ZReports
            .Where(z => z.PfaRegistrationId == command.PfaId && dates.Contains(z.Date))
            .ToListAsync(cancellationToken);
        foreach (ZReport report in reports)
        {
            await ZControls.CheckAsync(db, report, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }
}

/// <summary>Controalele raportului Z (spec flux contabil R12).</summary>
internal static class ZControls
{
    /// <summary>
    /// R12: bonurile zilei (aceeași casă, dacă se știe) se leagă de Z, iar suma lor trebuie să fie totalul
    /// Z. Altfel încasarea Z-ului intră la verificare. Fără bonuri nu e nimic de verificat.
    /// </summary>
    public static async Task CheckAsync(IApplicationDbContext db, ZReport report, CancellationToken cancellationToken)
    {
        List<FiscalReceipt> receipts = await db.FiscalReceipts
            .Where(r => r.PfaRegistrationId == report.PfaRegistrationId && r.Date == report.Date &&
                        (report.RegisterSerial == null || r.RegisterSerial == report.RegisterSerial) &&
                        (r.ZReportId == null || r.ZReportId == report.Id))
            .ToListAsync(cancellationToken);
        if (receipts.Count == 0)
        {
            return;
        }

        LedgerEntry? entry = report.LedgerEntryId is { } id
            ? db.LedgerEntries.Local.FirstOrDefault(e => e.Id == id) ?? await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            : null;
        bool ambiguousRegister = report.RegisterSerial == null && receipts.Select(r => r.RegisterSerial).Distinct().Skip(1).Any();
        if (!ambiguousRegister)
        {
            receipts.ForEach(r => r.ZReportId = report.Id);
        }
        if (entry is null || entry.Status == LedgerEntryStatus.Locked ||
            await db.PfaAccountingPeriods.AnyAsync(p => p.PfaRegistrationId == report.PfaRegistrationId &&
                p.Period == entry.AccountingPeriod && p.Status == AccountingPeriodStatus.Closed, cancellationToken))
        {
            return;
        }

        entry.ReconciliationStatus = !ambiguousRegister && receipts.Sum(r => r.Total) == report.Total
            ? ReconciliationStatus.Matched
            : ReconciliationStatus.NeedsReview;
    }

    /// <summary>Suma bonurilor legate de Z, pentru afișare lângă total.</summary>
    public static Task<decimal> ReceiptsTotalAsync(IApplicationDbContext db, Guid zReportId, CancellationToken cancellationToken) =>
        db.FiscalReceipts.Where(r => r.ZReportId == zReportId).SumAsync(r => r.Total, cancellationToken);
}
