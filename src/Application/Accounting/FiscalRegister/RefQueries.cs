using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Accounting.Contracts;
using Application.Accounting.Registers;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.FiscalRegister;

// Registrul de evidență fiscală (spec registre §4). Separat de RJIP prin namespace: REF citește
// tratamentul fiscal stabilit de Tax Engine (venit impozabil, deductibil, amortizare), niciodată RJIP-ul.
// Testul de arhitectură verifică lipsa dependenței.

/// <summary><c>GET /accounting/pfas/{pfaId}/registers/ref?year=</c>; <c>AsOf</c> = situație intermediară.</summary>
public sealed record GetRefQuery(Guid PfaId, int Year, DateOnly? AsOf = null) : IQuery<RefView>;

/// <summary>O lună de amortizare a unui activ, pentru REF.</summary>
internal sealed record RefDepreciation(Guid AssetId, DateOnly Month, string Label, decimal Amount);

/// <summary>
/// Registrul de evidență fiscală (OMFP 3254/2017, anexa 1), pe an și sursă de venit: venitul brut
/// (Σ venit impozabil), cheltuielile deductibile (Σ deductibil al plăților justificate + amortizarea
/// anului) și venitul net. Fiecare rând are drill-down până la înregistrări și linii de amortizare.
/// Status: <c>FINAL</c> după închiderea anului (snapshot-ul închiderii), <c>INTERMEDIATE</c> la o dată
/// (cerută sau încetarea colaborării), altfel <c>CURRENT</c> (provizoriu).
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

        if (query.AsOf is null && await FinalAsync(query.PfaId, query.Year, cancellationToken) is { } final)
        {
            return final;
        }

        (RefStatus status, DateOnly? asOf) = await StatusAsync(query, cancellationToken);
        return await ComputeAsync(db, query.PfaId, query.Year, status, asOf, cancellationToken);
    }

    /// <summary>REF-ul calculat din ledger și din amortizare, până la <paramref name="asOf"/> (sau tot anul).</summary>
    internal static async Task<RefView> ComputeAsync(
        IApplicationDbContext db, Guid pfaId, int year, RefStatus status, DateOnly? asOf, CancellationToken cancellationToken)
    {
        var start = new DateOnly(year, 1, 1);
        DateOnly end = asOf ?? new DateOnly(year, 12, 31);
        List<RegisterEntry> entries = await RegisterData.EntriesAsync(db, pfaId, start, end, cancellationToken);
        return Build(pfaId, year, status, asOf, entries, await DepreciationAsync(db, pfaId, year, end, cancellationToken));
    }

    /// <summary>Amortizarea fiscală a anului, lunile până la <paramref name="end"/> inclusiv (spec registre §6 pas 7).</summary>
    internal static async Task<List<RefDepreciation>> DepreciationAsync(IApplicationDbContext db, Guid pfaId, int year, DateOnly end, CancellationToken cancellationToken)
    {
        var lines = await db.DepreciationLines.AsNoTracking()
            .Where(l => l.PfaRegistrationId == pfaId && l.Year == year && l.Month <= end.Month)
            .Join(db.PfaAssets, l => l.AssetId, a => a.Id, (l, a) => new { l.AssetId, l.Year, l.Month, l.Amount, a.InventoryNumber, a.Name })
            .ToListAsync(cancellationToken);
        return [.. lines
            .OrderBy(l => l.Month).ThenBy(l => l.InventoryNumber, StringComparer.Ordinal)
            .Select(l => new RefDepreciation(l.AssetId, new DateOnly(l.Year, l.Month, 1), $"Amortizare {l.InventoryNumber} {l.Name}", l.Amount))];
    }

    internal static RefView Build(
        Guid pfaId, int year, RefStatus status, DateOnly? asOf, IEnumerable<RegisterEntry> entries, IEnumerable<RefDepreciation>? depreciation = null)
    {
        List<RegisterEntry> list = [.. entries];

        // Venitul brut: ce a stabilit Tax Engine ca impozabil (încasări efective; nu aporturi, transferuri,
        // rambursări sau payout-uri nereconciliate). Venitul Uber/Bolt e brutul din R21, nu payout-ul.
        List<RefContribution> income = [.. list
            .Where(e => e.TaxableLei != 0)
            .Select(e => new RefContribution(e.Entry.Id, null, e.Entry.Date, Label(e.Entry), e.TaxableLei))];

        // R01: o cheltuială se deduce doar justificată cu documente, în anul plății, cu suma deductibilă
        // (partea personală și achizițiile de mijloace fixe au deductibil 0); plus amortizarea anului.
        List<RefContribution> expenses = [.. list
            .Where(e => e.Entry.IsCashMovement &&
                        e.Entry.TransactionType == LedgerTransactionType.Expense &&
                        e.Entry.ReconciliationStatus is ReconciliationStatus.Matched or ReconciliationStatus.Partial &&
                        (e.DeductibleLei ?? 0) != 0)
            .Select(e => new RefContribution(e.Entry.Id, null, e.Entry.Date, Label(e.Entry), e.DeductibleLei!.Value)),
            .. (depreciation ?? []).Select(d => new RefContribution(null, d.AssetId, d.Month, d.Label, d.Amount))];

        decimal gross = income.Sum(c => c.Value);
        decimal deductible = expenses.Sum(c => c.Value);
        decimal net = gross - deductible;

        RefRow Row(string element, decimal value, IReadOnlyList<RefContribution>? contributions) => new(year, false, IncomeCategory, element, value, contributions);
        return new RefView(
            pfaId,
            year,
            status,
            asOf,
            [
                Row("Venit brut", gross, income),
                Row("Cheltuieli deductibile", deductible, expenses),
                net >= 0 ? Row("Venit net anual", net, null) : Row("Pierdere netă anuală", -net, null),
            ]);
    }

    private static string Label(LedgerEntry entry) =>
        entry.Counterparty is { Length: > 0 } counterparty && !entry.Description.Contains(counterparty, StringComparison.OrdinalIgnoreCase)
            ? $"{entry.Description} – {counterparty}"
            : entry.Description;

    /// <summary>Anul închis: REF-ul final e cel salvat la închidere, nu un calcul nou.</summary>
    private async Task<RefView?> FinalAsync(Guid pfaId, int year, CancellationToken cancellationToken)
    {
        string? json = await db.AccountingYears.AsNoTracking()
            .Where(y => y.PfaRegistrationId == pfaId && y.Year == year && y.Status == AccountingPeriodStatus.Closed)
            .Select(y => y.RefJson)
            .FirstOrDefaultAsync(cancellationToken);
        return AccountingJson.Deserialize<RefView?>(json, null) is { } view ? view with { Status = RefStatus.Final } : null;
    }

    private async Task<(RefStatus Status, DateOnly? AsOf)> StatusAsync(GetRefQuery query, CancellationToken cancellationToken)
    {
        if (query.AsOf is { } asOf)
        {
            return (RefStatus.Intermediate, asOf);
        }

        var engagement = await db.PfaAccountingEngagements.AsNoTracking()
            .Where(e => e.PfaRegistrationId == query.PfaId)
            .OrderByDescending(e => e.StartDate)
            .Select(e => new { e.EndDate, e.Status })
            .FirstOrDefaultAsync(cancellationToken);
        return engagement is { Status: EngagementStatus.Inactive, EndDate: { } end } && end.Year == query.Year
            ? (RefStatus.Intermediate, end)
            : (RefStatus.Current, null);
    }
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
            _ => "Calcul curent (provizoriu)",
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
            ["Cheltuielile deductibile sunt sumele deductibile (după regula de deductibilitate valabilă la data fiecărei cheltuieli), nu sumele plătite, plus amortizarea fiscală a anului."]);
        string suffix = data.Status == RefStatus.Intermediate ? $"_{data.AsOf:yyyyMMdd}" : string.Empty;
        return RegisterFiles.Export(exporter, document, query.Format, $"REF_{cui}_{data.Year}{suffix}");
    }
}
