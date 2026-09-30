using System.Globalization;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Registers;

public enum RegisterFormat
{
    Pdf = 0,
    Xlsx = 1,
}

public sealed record RegisterFile(byte[] Content, string FileName, string ContentType);

internal static class RegisterErrors
{
    public static readonly Error InvalidRange = Error.Problem("Accounting.InvalidRange", "Intervalul nu e valid: „de la” trebuie să fie înainte de „până la”, în cel mult 3 ani.");

    public static readonly Error InvalidYear = Error.Problem("Accounting.InvalidYear", "Anul nu e valid.");
}

// ─── RJIP (cod 14-1-1/b) ─────────────────────────────────────────────────────────────────────────

/// <summary><c>GET /accounting/pfas/{pfaId}/registers/rjip?from&amp;to</c></summary>
public sealed record GetRjipQuery(Guid PfaId, DateOnly From, DateOnly To) : IQuery<RjipView>;

/// <summary>
/// Registrul-jurnal de încasări și plăți (OMFP 170/2015, cod 14-1-1/b): cronologic, fiecare
/// operațiune distinctă, cu sumele efectiv încasate sau plătite, în numerar sau prin bancă, în lei,
/// totalizate lunar.
/// </summary>
internal sealed class GetRjipQueryHandler(IApplicationDbContext db, Microsoft.Extensions.Options.IOptions<AccountingOptions>? options = null)
    : IQueryHandler<GetRjipQuery, RjipView>
{
    public async Task<Result<RjipView>> Handle(GetRjipQuery query, CancellationToken cancellationToken)
    {
        if (query.From > query.To || query.To.DayNumber - query.From.DayNumber > 3 * 366)
        {
            return Result.Failure<RjipView>(RegisterErrors.InvalidRange);
        }

        if (await RegisterData.PfaAsync(db, query.PfaId, cancellationToken) is null)
        {
            return Result.Failure<RjipView>(AccountingErrors.PfaNotFound);
        }

        return Build(
            query.PfaId, query.From, query.To,
            await RegisterData.EntriesAsync(db, query.PfaId, query.From, query.To, cancellationToken),
            options?.Value.ManualChannelMapping ?? ManualChannelMapping.OwnerContributionAndCash);
    }

    internal static RjipView Build(
        Guid pfaId, DateOnly from, DateOnly to, IEnumerable<RegisterEntry> entries,
        ManualChannelMapping manual = ManualChannelMapping.OwnerContributionAndCash)
    {
        List<RjipRow> rows = [.. entries
            .Where(e => RegisterData.IsCashMovement(e.Entry) && e.AmountLei != 0)
            .SelectMany(e => Rows(e, manual))];
        List<RjipMonthTotal> totals = [.. rows
            .GroupBy(row => RegisterData.PeriodOf(row.Date))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new RjipMonthTotal(
                group.Key,
                group.Sum(row => row.CashIn),
                group.Sum(row => row.CashOut),
                group.Sum(row => row.BankIn),
                group.Sum(row => row.BankOut)))];
        return new RjipView(pfaId, from, to, rows, totals);
    }

    /// <summary>
    /// Rândurile unei înregistrări. O plată cu card sau cont neconectat (Q1) nu are coloana ei: după
    /// configurare, e o plată în numerar precedată de aportul titularului, doar numerar sau doar bancă.
    /// </summary>
    private static IEnumerable<RjipRow> Rows(RegisterEntry item, ManualChannelMapping manual)
    {
        if (item.Entry.PaymentMethod != PaymentMethod.Manual)
        {
            yield return Row(item, item.Entry.PaymentMethod == PaymentMethod.Cash);
            yield break;
        }

        if (manual == ManualChannelMapping.OwnerContributionAndCash && item.AmountLei < 0)
        {
            decimal value = Math.Abs(item.AmountLei);
            yield return new RjipRow(
                item.Entry.Id, item.Entry.Date, item.Entry.DocumentLabel,
                "Aport titular (plată cu card sau cont neconectat)", value, 0, 0, 0);
        }

        yield return Row(item, manual != ManualChannelMapping.Bank);
    }

    private static RjipRow Row(RegisterEntry item, bool cash)
    {
        LedgerEntry entry = item.Entry;
        decimal value = Math.Abs(item.AmountLei);
        bool incoming = item.AmountLei > 0;
        string operation = entry.Counterparty is { Length: > 0 } counterparty && !entry.Description.Contains(counterparty, StringComparison.OrdinalIgnoreCase)
            ? $"{entry.Description} – {counterparty}"
            : entry.Description;
        if (item.CurrencyNote is not null)
        {
            operation += $" ({item.CurrencyNote})";
        }

        return new RjipRow(
            entry.Id,
            entry.Date,
            entry.DocumentLabel,
            operation,
            cash && incoming ? value : 0,
            cash && !incoming ? value : 0,
            !cash && incoming ? value : 0,
            !cash && !incoming ? value : 0);
    }
}

// ─── REF (OMFP 3254/2017) ────────────────────────────────────────────────────────────────────────

/// <summary><c>GET /accounting/pfas/{pfaId}/registers/ref?year=</c>; <c>AsOf</c> = situație intermediară.</summary>
public sealed record GetRefQuery(Guid PfaId, int Year, DateOnly? AsOf = null) : IQuery<RefView>;

/// <summary>
/// Registrul de evidență fiscală (OMFP 3254/2017, anexa 1): anul, rectificarea, sursa / categoria
/// venitului și elementele de calcul ale venitului net anual. Folosește sumele <b>deductibile</b>.
/// Status: <c>FINAL</c> când toate lunile de activitate ale anului sunt închise,
/// <c>INTERMEDIATE</c> la o dată (cerută sau data încetării colaborării), altfel <c>CURRENT</c>.
/// </summary>
internal sealed class GetRefQueryHandler(IApplicationDbContext db) : IQueryHandler<GetRefQuery, RefView>
{
    /// <summary>Categoria de venit din Codul fiscal (titlul IV) și sursa, pe fila registrului (art. 3 alin. 3).</summary>
    public const string IncomeCategory = "Venituri din activități independente – transport alternativ (ridesharing)";

    public async Task<Result<RefView>> Handle(GetRefQuery query, CancellationToken cancellationToken)
    {
        if (query.Year is < 2000 or > 2100 || query.AsOf is { } date && date.Year != query.Year)
        {
            return Result.Failure<RefView>(RegisterErrors.InvalidYear);
        }

        if (await RegisterData.PfaAsync(db, query.PfaId, cancellationToken) is null)
        {
            return Result.Failure<RefView>(AccountingErrors.PfaNotFound);
        }

        (RefStatus status, DateOnly? asOf) = await StatusAsync(query, cancellationToken);
        var start = new DateOnly(query.Year, 1, 1);
        DateOnly end = asOf ?? new DateOnly(query.Year, 12, 31);
        List<RegisterEntry> entries = await RegisterData.EntriesAsync(db, query.PfaId, start, end, cancellationToken);
        return Build(query.PfaId, query.Year, status, asOf, entries);
    }

    internal static RefView Build(Guid pfaId, int year, RefStatus status, DateOnly? asOf, IEnumerable<RegisterEntry> entries)
    {
        // Doar încasările și plățile efective (§7): payout-ul nereconciliat nu e venit (invariantul 4),
        // aporturile, retragerile titularului și transferurile nu sunt nici venit, nici cheltuială.
        List<RegisterEntry> list = [.. entries.Where(e => RegisterData.IsCashMovement(e.Entry))];
        decimal gross = list.Where(e => e.Entry.TransactionType == LedgerTransactionType.Income).Sum(e => e.AmountLei);
        // R01: o cheltuială se deduce doar justificată cu documente — fără document („Document lipsă”)
        // sau la verificare, suma ei deductibilă e doar o propunere și nu intră în registru.
        decimal deductible = list
            .Where(e => e.Entry.TransactionType == LedgerTransactionType.Expense &&
                        e.Entry.ReconciliationStatus is ReconciliationStatus.Matched or ReconciliationStatus.Partial)
            .Sum(e => e.DeductibleLei ?? 0);
        decimal net = gross - deductible;

        RefRow Row(string element, decimal value) => new(year, false, IncomeCategory, element, value);
        return new RefView(
            pfaId,
            year,
            status,
            asOf,
            [
                Row("Venit brut", gross),
                Row("Cheltuieli deductibile", deductible),
                net >= 0 ? Row("Venit net anual", net) : Row("Pierdere netă anuală", -net),
            ]);
    }

    private async Task<(RefStatus Status, DateOnly? AsOf)> StatusAsync(GetRefQuery query, CancellationToken cancellationToken)
    {
        var engagement = await db.PfaAccountingEngagements.AsNoTracking()
            .Where(e => e.PfaRegistrationId == query.PfaId)
            .OrderByDescending(e => e.StartDate)
            .Select(e => new { e.StartDate, e.EndDate, e.Status })
            .FirstOrDefaultAsync(cancellationToken);

        // Lunile de activitate ale anului: de la începutul colaborării până la încheierea ei.
        var first = new DateOnly(query.Year, 1, 1);
        var last = new DateOnly(query.Year, 12, 1);
        if (engagement is not null && engagement.StartDate > first)
        {
            first = new DateOnly(engagement.StartDate.Year, engagement.StartDate.Month, 1);
        }

        if (engagement?.EndDate is { } endDate && endDate < last.AddMonths(1))
        {
            last = new DateOnly(endDate.Year, endDate.Month, 1);
        }

        List<string> months = [];
        for (DateOnly month = first; month <= last; month = month.AddMonths(1))
        {
            months.Add(RegisterData.PeriodOf(month));
        }

        int closed = await db.PfaAccountingPeriods.CountAsync(
            p => p.PfaRegistrationId == query.PfaId && months.Contains(p.Period) && p.Status == AccountingPeriodStatus.Closed,
            cancellationToken);

        if (query.AsOf is { } asOf)
        {
            return (RefStatus.Intermediate, asOf);
        }

        if (months.Count > 0 && closed == months.Count)
        {
            return (RefStatus.Final, null);
        }

        return engagement is { Status: EngagementStatus.Inactive, EndDate: { } end } && end.Year == query.Year
            ? (RefStatus.Intermediate, end)
            : (RefStatus.Current, null);
    }
}

// ─── Registrul-inventar (cod 14-1-2/b) ──────────────────────────────────────────────────────────

/// <summary><c>GET /accounting/pfas/{pfaId}/registers/inventory?year=</c> — activele folosite în an.</summary>
public sealed record GetInventoryQuery(Guid PfaId, int Year) : IQuery<InventoryView>;

internal sealed class GetInventoryQueryHandler(IApplicationDbContext db) : IQueryHandler<GetInventoryQuery, InventoryView>
{
    public async Task<Result<InventoryView>> Handle(GetInventoryQuery query, CancellationToken cancellationToken)
    {
        if (query.Year is < 2000 or > 2100)
        {
            return Result.Failure<InventoryView>(RegisterErrors.InvalidYear);
        }

        if (await RegisterData.PfaAsync(db, query.PfaId, cancellationToken) is null)
        {
            return Result.Failure<InventoryView>(AccountingErrors.PfaNotFound);
        }

        var start = new DateOnly(query.Year, 1, 1);
        var end = new DateOnly(query.Year, 12, 31);
        List<AssetDto> assets = await Assets.DtosAsync(
            db,
            db.PfaAssets.Where(a => a.PfaRegistrationId == query.PfaId && a.AcquisitionDate <= end && (a.DisposedDate == null || a.DisposedDate >= start)),
            cancellationToken);
        return new InventoryView(query.PfaId, query.Year, assets);
    }
}

// ─── Exporturi ──────────────────────────────────────────────────────────────────────────────────

/// <summary><c>GET …/registers/rjip/export?from&amp;to&amp;format</c></summary>
public sealed record ExportRjipQuery(Guid PfaId, DateOnly From, DateOnly To, RegisterFormat Format) : IQuery<RegisterFile>;

internal sealed class ExportRjipQueryHandler(IApplicationDbContext db, IQueryHandler<GetRjipQuery, RjipView> rjip, IRegisterExporter exporter)
    : IQueryHandler<ExportRjipQuery, RegisterFile>
{
    public async Task<Result<RegisterFile>> Handle(ExportRjipQuery query, CancellationToken cancellationToken)
    {
        Result<RjipView> view = await rjip.Handle(new GetRjipQuery(query.PfaId, query.From, query.To), cancellationToken);
        if (view.IsFailure)
        {
            return Result.Failure<RegisterFile>(view.Error);
        }

        (string name, string cui) = (await RegisterData.PfaAsync(db, query.PfaId, cancellationToken))!.Value;
        var lines = new List<RegisterLine>();
        int number = 0;
        foreach (IGrouping<string, RjipRow> month in view.Value.Rows.GroupBy(row => RegisterData.PeriodOf(row.Date)))
        {
            lines.AddRange(month.Select(row => new RegisterLine(
                [++number, RegisterData.Date(row.Date), row.Document, row.Operation, Cell(row.CashIn), Cell(row.BankIn), Cell(row.CashOut), Cell(row.BankOut)])));
            RjipMonthTotal total = view.Value.MonthTotals.Single(t => t.Period == month.Key);
            lines.Add(new RegisterLine(
                [null, null, null, $"Total {MonthName(month.Key)}", total.CashIn, total.BankIn, total.CashOut, total.BankOut], Emphasis: true));
        }

        var document = new RegisterDocument(
            "REGISTRUL-JURNAL DE ÎNCASĂRI ȘI PLĂȚI",
            "14-1-1/b",
            [$"{name} — CUI {cui}", $"Perioada {RegisterData.Date(query.From)} – {RegisterData.Date(query.To)}", "Sume în lei"],
            [
                new("Nr. crt.", Width: 0.5f),
                new("Data operațiunii de încasare/plată", Width: 1.1f),
                new("Documentul (fel, număr)", Width: 1.4f),
                new("Explicații", Width: 2.6f),
                new("Încasări — Numerar", Numeric: true),
                new("Încasări — Bancă", Numeric: true),
                new("Plăți — Numerar", Numeric: true),
                new("Plăți — Bancă", Numeric: true),
            ],
            ["0", "1", "2", "3", "4", "5", "6", "7"],
            lines,
            ["Model conform OMFP nr. 170/2015. Sumele în valută sunt trecute în lei la cursul BNR din ultima zi bancară anterioară operațiunii."]);
        return Export(exporter, document, query.Format, $"RJIP_{cui}_{query.From:yyyyMMdd}_{query.To:yyyyMMdd}");
    }

    private static decimal? Cell(decimal value) => value == 0 ? null : value;

    private static string MonthName(string period)
    {
        var date = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return date.ToString("MMMM yyyy", RegisterData.Ro);
    }

    internal static RegisterFile Export(IRegisterExporter exporter, RegisterDocument document, RegisterFormat format, string name) =>
        format == RegisterFormat.Pdf
            ? new RegisterFile(exporter.ToPdf(document), $"{name}.pdf", "application/pdf")
            : new RegisterFile(exporter.ToXlsx(document), $"{name}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
}

/// <summary><c>GET …/registers/ref/export?year&amp;format&amp;asOf?</c></summary>
public sealed record ExportRefQuery(Guid PfaId, int Year, RegisterFormat Format, DateOnly? AsOf) : IQuery<RegisterFile>;

internal sealed class ExportRefQueryHandler(IApplicationDbContext db, IQueryHandler<GetRefQuery, RefView> refView, IRegisterExporter exporter)
    : IQueryHandler<ExportRefQuery, RegisterFile>
{
    public async Task<Result<RegisterFile>> Handle(ExportRefQuery query, CancellationToken cancellationToken)
    {
        Result<RefView> view = await refView.Handle(new GetRefQuery(query.PfaId, query.Year, query.AsOf), cancellationToken);
        if (view.IsFailure)
        {
            return Result.Failure<RegisterFile>(view.Error);
        }

        (string name, string cui) = (await RegisterData.PfaAsync(db, query.PfaId, cancellationToken))!.Value;
        RefView data = view.Value;
        string status = data.Status switch
        {
            RefStatus.Final => "Final",
            RefStatus.Intermediate => $"Situație intermediară la {RegisterData.Date(data.AsOf!.Value)}",
            _ => "Calcul curent",
        };
        var document = new RegisterDocument(
            "REGISTRUL DE EVIDENȚĂ FISCALĂ",
            "Anexa 1 la OMFP nr. 3254/2017",
            [
                $"{name} — CUI {cui}",
                $"Anul {data.Year}",
                $"Rectificare: {(data.Rows.Any(r => r.Rectification) ? "Da" : "Nu")}",
                $"Sursa de venit/categorie de venit: {GetRefQueryHandler.IncomeCategory}",
                status,
            ],
            [
                new("Nr. crt.", Width: 0.5f),
                new("Elemente de calcul pentru stabilirea venitului net anual/pierderii nete anuale", Width: 4f),
                new("Valoare - lei -", Numeric: true, Width: 1.2f),
            ],
            null,
            [.. data.Rows.Select((row, index) => new RegisterLine([index + 1, row.CalculationElement, row.Value], Emphasis: index == data.Rows.Count - 1))],
            ["Cheltuielile deductibile sunt sumele deductibile (după regula de deductibilitate valabilă la data fiecărei cheltuieli), nu sumele plătite."]);
        string suffix = data.Status == RefStatus.Intermediate ? $"_{data.AsOf:yyyyMMdd}" : string.Empty;
        return ExportRjipQueryHandler.Export(exporter, document, query.Format, $"REF_{cui}_{data.Year}{suffix}");
    }
}

/// <summary><c>GET …/registers/inventory/export?year&amp;format</c></summary>
public sealed record ExportInventoryQuery(Guid PfaId, int Year, RegisterFormat Format) : IQuery<RegisterFile>;

/// <summary>
/// Registrul-inventar (OMFP 170/2015, cod 14-1-2/b), la sfârșitul anului sau la data încetării
/// colaborării, dacă a încetat în acel an: elementele inventariate și valoarea de inventar
/// (valoarea de intrare, din documentele justificative).
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

        DateOnly? ended = await db.PfaAccountingEngagements.AsNoTracking()
            .Where(e => e.PfaRegistrationId == query.PfaId && e.Status == EngagementStatus.Inactive && e.EndDate != null)
            .OrderByDescending(e => e.EndDate)
            .Select(e => e.EndDate)
            .FirstOrDefaultAsync(cancellationToken);
        DateOnly date = ended is { } end && end.Year == query.Year ? end : new DateOnly(query.Year, 12, 31);

        List<PfaAsset> assets = await db.PfaAssets.AsNoTracking()
            .Where(a => a.PfaRegistrationId == query.PfaId && a.AcquisitionDate <= date && (a.DisposedDate == null || a.DisposedDate > date))
            .OrderBy(a => a.AcquisitionDate)
            .ToListAsync(cancellationToken);

        List<RegisterLine> lines = [.. assets.Select((asset, index) => new RegisterLine(
            [index + 1, $"{asset.Type} — {asset.Description} (intrat la {RegisterData.Date(asset.AcquisitionDate)})", asset.AcquisitionValue]))];
        lines.Add(new RegisterLine([null, "Total", assets.Sum(a => a.AcquisitionValue)], Emphasis: true));

        var document = new RegisterDocument(
            "REGISTRUL-INVENTAR",
            "14-1-2/b",
            [$"{pfa.Name} — CUI {pfa.Cui}", $"la data de {RegisterData.Date(date)}"],
            [
                new("Nr. crt.", Width: 0.5f),
                new("Denumirea elementelor inventariate", Width: 4f),
                new("Valoarea de inventar", Numeric: true, Width: 1.2f),
            ],
            ["1", "2", "3"],
            lines,
            ["Model conform OMFP nr. 170/2015. Creanțele și datoriile inventariate nu sunt încă evidențiate în aplicație."]);
        return ExportRjipQueryHandler.Export(exporter, document, query.Format, $"Registru-inventar_{pfa.Cui}_{date:yyyyMMdd}");
    }
}
