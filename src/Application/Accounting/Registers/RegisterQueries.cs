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
    Csv = 2,
}

public sealed record RegisterFile(byte[] Content, string FileName, string ContentType);

/// <summary>Fișierul unui registru, în formatul cerut.</summary>
internal static class RegisterFiles
{
    public static RegisterFile Export(IRegisterExporter exporter, RegisterDocument document, RegisterFormat format, string name) => format switch
    {
        RegisterFormat.Pdf => new RegisterFile(exporter.ToPdf(document), $"{name}.pdf", "application/pdf"),
        RegisterFormat.Csv => new RegisterFile(RegisterCsv.Write(document), $"{name}.csv", "text/csv"),
        _ => new RegisterFile(exporter.ToXlsx(document), $"{name}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
    };
}

internal static class RegisterErrors
{
    public static readonly Error InvalidRange = Error.Problem("Accounting.InvalidRange", "Intervalul nu e valid: „de la” trebuie să fie înainte de „până la”, în cel mult 3 ani.");

    public static readonly Error InvalidYear = Error.Problem("Accounting.InvalidYear", "Anul nu e valid.");
}

// ─── RJIP (cod 14-1-1/b) ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// <c>GET /accounting/pfas/{pfaId}/registers/rjip?from&amp;to&amp;regenerate</c>. Lunile închise vin din
/// snapshot-ul închiderii (spec registre §3); <c>regenerate</c> le reface din ledger, pentru control.
/// </summary>
public sealed record GetRjipQuery(Guid PfaId, DateOnly From, DateOnly To, bool Regenerate = false) : IQuery<RjipView>;

/// <summary>
/// Registrul-jurnal de încasări și plăți (OMFP 170/2015, cod 14-1-1/b): cronologic, fiecare
/// operațiune distinctă, cu sumele efectiv încasate sau plătite, în numerar sau prin bancă, în lei,
/// totalizate lunar. Citește ledger-ul direct, la valori integrale.
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

        List<RegisterEntry> entries = await RegisterData.EntriesAsync(db, query.PfaId, query.From, query.To, cancellationToken);
        RjipView live = Build(
            query.PfaId, query.From, query.To, entries,
            options?.Value.ManualChannelMapping ?? ManualChannelMapping.OwnerContributionAndCash,
            await RjipSources.LoadAsync(db, entries, cancellationToken));
        return query.Regenerate ? live : await WithSnapshotsAsync(live, cancellationToken);
    }

    /// <summary>
    /// Lunile închise, așa cum au fost la închidere: rândurile din ultimul snapshot al lunii, în locul
    /// celor regenerate. Pentru o lună închisă cele două coincid (testul de acceptanță din §3).
    /// </summary>
    private async Task<RjipView> WithSnapshotsAsync(RjipView live, CancellationToken cancellationToken)
    {
        List<string> periods = [.. Months(live.From, live.To)];
        List<string> closed = await db.PfaAccountingPeriods.AsNoTracking()
            .Where(p => p.PfaRegistrationId == live.PfaId && periods.Contains(p.Period) && p.Status == AccountingPeriodStatus.Closed)
            .Select(p => p.Period)
            .ToListAsync(cancellationToken);
        if (closed.Count == 0)
        {
            return live;
        }

        List<AccountingPeriodSnapshot> snapshots = await db.AccountingPeriodSnapshots.AsNoTracking()
            .Where(s => s.PfaRegistrationId == live.PfaId && closed.Contains(s.Period))
            .ToListAsync(cancellationToken);
        var frozen = snapshots
            .GroupBy(s => s.Period)
            .Select(group => group.OrderByDescending(s => s.CreatedAtUtc).First())
            .Select(s => AccountingJson.Deserialize<RjipView?>(s.RjipJson, null))
            .OfType<RjipView>()
            .ToDictionary(view => RegisterData.PeriodOf(view.From));
        if (frozen.Count == 0)
        {
            return live;
        }

        IEnumerable<RjipRow> rows = live.Rows
            .Where(row => !frozen.ContainsKey(RegisterData.PeriodOf(row.Date)))
            .Concat(frozen.Values.SelectMany(view => view.Rows).Where(row => row.Date >= live.From && row.Date <= live.To));
        return WithTotals(live.PfaId, live.From, live.To, [.. rows]);
    }

    private static IEnumerable<string> Months(DateOnly from, DateOnly to)
    {
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            yield return RegisterData.PeriodOf(month);
        }
    }

    internal static RjipView Build(
        Guid pfaId, DateOnly from, DateOnly to, IEnumerable<RegisterEntry> entries,
        ManualChannelMapping manual = ManualChannelMapping.OwnerContributionAndCash,
        RjipSources? sources = null)
    {
        RjipSources known = sources ?? RjipSources.Empty;
        List<RjipRow> rows = [.. entries
            .Where(e => RegisterData.IsCashMovement(e.Entry) && e.AmountLei != 0)
            .SelectMany(e => Rows(e, manual, known))];
        return WithTotals(pfaId, from, to, rows);
    }

    /// <summary>Totalurile lunare și Nr. crt., continuu în fiecare lună, în ordinea datelor.</summary>
    private static RjipView WithTotals(Guid pfaId, DateOnly from, DateOnly to, List<RjipRow> unordered)
    {
        List<RjipRow> rows = [.. unordered
            .OrderBy(row => row.Date)
            .GroupBy(row => RegisterData.PeriodOf(row.Date))
            .SelectMany(month => month.Select((row, index) => row with { No = index + 1 }))];
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
    private static IEnumerable<RjipRow> Rows(RegisterEntry item, ManualChannelMapping manual, RjipSources sources)
    {
        if (item.Entry.PaymentMethod != PaymentMethod.Manual)
        {
            yield return Row(item, item.Entry.PaymentMethod == PaymentMethod.Cash, sources);
            yield break;
        }

        (bool incoming, decimal value) = Column(item);
        if (manual == ManualChannelMapping.OwnerContributionAndCash && !incoming)
        {
            yield return new RjipRow(
                item.Entry.Id, item.Entry.Date, item.Entry.DocumentLabel,
                "Aport titular (plată cu card sau cont neconectat)", value, 0, 0, 0);
        }

        yield return Row(item, manual != ManualChannelMapping.Bank, sources);
    }

    /// <summary>
    /// Coloana și suma rândului. Stornarea (§4) stă pe coloana operațiunii anulate, cu minus: stornarea
    /// unei plăți de 250 e −250 la plăți, nu 250 la încasări.
    /// </summary>
    private static (bool Incoming, decimal Value) Column(RegisterEntry item) =>
        item.Entry.StornoOfEntryId is null
            ? (item.AmountLei > 0, Math.Abs(item.AmountLei))
            : (item.AmountLei < 0, -Math.Abs(item.AmountLei));

    /// <summary>
    /// Coloana Document (spec registre §3, Q1): la bancă, „Extras bancar”, iar justificativul (factura,
    /// bonul) trece în explicații; în numerar, documentul e chiar justificativul (Raport Z, bon fiscal).
    /// </summary>
    public const string BankStatement = "Extras bancar";

    private static RjipRow Row(RegisterEntry item, bool cash, RjipSources sources)
    {
        LedgerEntry entry = item.Entry;
        (bool incoming, decimal value) = Column(item);
        string operation = RjipExplanations.Explain(entry, sources);
        string document = RjipExplanations.Document(entry, sources);

        if (item.CurrencyNote is not null)
        {
            operation += $" ({item.CurrencyNote})";
        }

        return new RjipRow(
            entry.Id,
            entry.Date,
            document,
            operation,
            cash && incoming ? value : 0,
            cash && !incoming ? value : 0,
            !cash && incoming ? value : 0,
            !cash && !incoming ? value : 0,
            0,
            RjipExplanations.ExceptionOf(entry),
            RjipExplanations.BankDetails(entry, sources),
            entry.ProposedClassification);
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
        foreach (IGrouping<string, RjipRow> month in view.Value.Rows.GroupBy(row => RegisterData.PeriodOf(row.Date)))
        {
            lines.AddRange(month.Select(row => new RegisterLine(
                [row.No, RegisterData.Date(row.Date), row.Document, row.Operation, Cell(row.CashIn), Cell(row.BankIn), Cell(row.CashOut), Cell(row.BankOut)])));
            RjipMonthTotal total = view.Value.MonthTotals.Single(t => t.Period == month.Key);
            lines.Add(new RegisterLine(
                [null, null, null, $"Total {MonthName(month.Key)}", total.CashIn, total.BankIn, total.CashOut, total.BankOut], Emphasis: true));
        }

        if (view.Value.MonthTotals.Count > 1)
        {
            IReadOnlyList<RjipMonthTotal> months = view.Value.MonthTotals;
            lines.Add(new RegisterLine(
                [null, null, null, $"Total {Span(query.From, query.To)}", months.Sum(t => t.CashIn), months.Sum(t => t.BankIn), months.Sum(t => t.CashOut), months.Sum(t => t.BankOut)],
                Emphasis: true));
        }

        var document = new RegisterDocument(
            "REGISTRUL-JURNAL DE ÎNCASĂRI ȘI PLĂȚI",
            "14-1-1/b",
            [$"{name} — CUI {cui}", Span(query.From, query.To), "Sume în lei"],
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
        return RegisterFiles.Export(exporter, document, query.Format, $"RJIP_{cui}_{query.From:yyyyMMdd}_{query.To:yyyyMMdd}");
    }

    private static decimal? Cell(decimal value) => value == 0 ? null : value;

    /// <summary>Antetul: „Anul 2026”, „Luna octombrie 2026” sau intervalul.</summary>
    private static string Span(DateOnly from, DateOnly to)
    {
        bool wholeMonths = from.Day == 1 && to == new DateOnly(to.Year, to.Month, DateTime.DaysInMonth(to.Year, to.Month));
        if (wholeMonths && from.Month == 1 && to.Month == 12 && from.Year == to.Year)
        {
            return $"Anul {from.Year}";
        }

        return wholeMonths && from.Year == to.Year && from.Month == to.Month
            ? $"Luna {MonthName(RegisterData.PeriodOf(from))}"
            : $"Perioada {RegisterData.Date(from)} – {RegisterData.Date(to)}";
    }

    private static string MonthName(string period)
    {
        var date = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return date.ToString("MMMM yyyy", RegisterData.Ro);
    }


}
