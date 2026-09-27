using Application.Abstractions.Data;
using Application.Accounting.Contracts;
using Application.Accounting.Tax;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace Application.Accounting.Months;

public sealed record PreCheckResult(PfaMonthStatus Status, IReadOnlyList<string> Reasons)
{
    public bool IsReady => Status == PfaMonthStatus.Ready;
}

/// <summary>
/// Pre-check-ul unui PFA pe o lună (spec contabilitate B3): datele PFA-ului, documentele așteptate
/// prezente și confirmate, art. 317 activ, apoi regulile (prin motorul fiscal). Documentul
/// lipsă are prioritate față de „de verificat”.
/// </summary>
/// <remarks>
/// Lista completă e în secțiunea 10 a documentului clientului, care nu e încă în repo. Duplicatul,
/// furnizorul, codul TVA, valoarea, moneda, perioada, corelarea și „nedeclarat anterior” sunt
/// verificările documentului (B1): un document confirmat le-a trecut.
/// </remarks>
internal static class PreCheck
{
    private static readonly (PlatformDocumentType Type, string Label)[] Slots =
    [
        (PlatformDocumentType.CommissionInvoice, "factura de comision"),
        (PlatformDocumentType.PlatformReport, "raportul lunar"),
    ];

    public static PreCheckResult Evaluate(ScopePfa pfa, MonthData data, TaxEngineSettings settings)
    {
        var missing = new List<string>();
        var review = new List<string>();
        List<MonthDocument> documents = [.. data.DocumentsOf(pfa.Id)];

        if (string.IsNullOrWhiteSpace(pfa.Cui))
        {
            review.Add("CIF-ul PFA-ului lipsește.");
        }

        IReadOnlyList<Platform>? platforms = data.PlatformsOf(pfa.Id);
        if (platforms is null || platforms.Count == 0)
        {
            review.Add("Platformele PFA-ului nu sunt setate (Setări contabilitate).");
        }
        else
        {
            foreach (Platform platform in platforms)
            {
                foreach ((PlatformDocumentType type, string label) in Slots)
                {
                    List<MonthDocument> present = [.. documents.Where(d =>
                        d.Document.Platform == platform &&
                        d.Document.DocumentType == type &&
                        d.Document.Status != PlatformDocumentStatus.ExtractionFailed)];
                    if (present.Count == 0)
                    {
                        missing.Add($"Lipsește {label} {PlatformName(platform)}.");
                    }
                    else if (type == PlatformDocumentType.CommissionInvoice)
                    {
                        missing.AddRange(InvoiceCoverageGaps(data.Period, platform, present, settings.VatExigibility));
                    }
                }
            }
        }

        int pending = 0;
        foreach (MonthDocument document in documents)
        {
            switch (document.Document.Status)
            {
                case PlatformDocumentStatus.Confirmed or PlatformDocumentStatus.Locked:
                    break;
                case PlatformDocumentStatus.Uploaded or PlatformDocumentStatus.Extracting:
                    review.Add($"{document.FileName}: încă necitit.");
                    break;
                case PlatformDocumentStatus.ExtractionFailed:
                    review.Add($"{document.FileName}: citire eșuată.");
                    break;
                case PlatformDocumentStatus.PendingConfirmation:
                    pending++;
                    break;
                default:
                    DocumentCheck? failed = AccountingJson.Deserialize<List<DocumentCheck>>(document.Extraction?.ChecksResultJson, [])
                        .FirstOrDefault(check => !check.Passed);
                    review.Add(failed is null ? $"{MonthData.Label(document)}: de verificat." : $"{MonthData.Label(document)}: {failed.Message}");
                    break;
            }
        }

        // După problemele reale: un document fără probleme doar așteaptă confirmarea.
        if (pending > 0)
        {
            review.Add(pending == 1 ? "Un document așteaptă confirmare." : $"{pending} documente așteaptă confirmare.");
        }

        if (!data.Art317ActiveAtEnd(pfa.Id))
        {
            review.Add("Codul special de TVA art. 317 nu e activ în perioadă (Setări contabilitate).");
        }

        if (missing.Count == 0 && review.Count == 0)
        {
            review.AddRange(MonthlyTaxEngine.Calculate(data.TaxInput(pfa.Id, settings)).BlockingReasons);
        }

        PfaMonthStatus status = PfaMonthStatus.Ready;
        if (missing.Count > 0)
        {
            status = PfaMonthStatus.MissingDocuments;
        }
        else if (review.Count > 0)
        {
            status = PfaMonthStatus.NeedsReview;
        }

        return new PreCheckResult(status, [.. missing, .. review]);
    }

    private static string PlatformName(Platform platform) => platform == Platform.Bolt ? "Bolt" : "Uber";

    /// <summary>
    /// Facturile de comision ale unei platforme trebuie să acopere luna fără goluri: Uber emite una pe
    /// săptămână, Bolt una pe lună, deci nu se numără facturile, ci se urmăresc perioadele lor.
    /// <list type="bullet">
    /// <item>între două facturi consecutive nu rămân zile nefacturate;</item>
    /// <item>cu regula „data impozitării” / „sfârșitul perioadei”, luna începe cu factura care conține
    /// ziua 1 și se termină cu ultima a cărei perioadă următoare (de aceeași lungime) ar cădea în luna
    /// următoare — altfel lipsește o factură la margine.</item>
    /// </list>
    /// Facturile fără perioadă citită nu intră în verificare (le semnalează documentul însuși).
    /// </summary>
    internal static IEnumerable<string> InvoiceCoverageGaps(string period, Platform platform, IReadOnlyList<MonthDocument> invoices, VatExigibilityRule rule)
    {
        List<(DateOnly From, DateOnly To)> spans = [.. invoices
            .Where(d => d.Extraction is { PeriodFrom: not null, PeriodTo: not null })
            .Select(d => (From: d.Extraction!.PeriodFrom!.Value, To: d.Extraction.PeriodTo!.Value))
            .Where(span => span.From <= span.To)
            .OrderBy(span => span.From)];
        if (spans.Count == 0)
        {
            yield break;
        }

        (DateOnly start, DateOnly end) = AccountingScope.Bounds(period);
        string name = PlatformName(platform);
        bool edges = rule != VatExigibilityRule.InvoiceDate;

        if (edges && spans[0].From > start)
        {
            yield return $"Lipsește factura de comision {name} pentru perioada de dinainte de {AccountingJson.Date(spans[0].From)}.";
        }

        DateOnly coveredTo = spans[0].To;
        foreach ((DateOnly from, DateOnly to) in spans.Skip(1))
        {
            if (from > coveredTo.AddDays(1))
            {
                yield return $"Lipsește factura de comision {name} pentru {AccountingJson.Date(coveredTo.AddDays(1))}–{AccountingJson.Date(from.AddDays(-1))}.";
            }

            coveredTo = to > coveredTo ? to : coveredTo;
        }

        (DateOnly lastFrom, DateOnly lastTo) = spans[^1];
        int length = lastTo.DayNumber - lastFrom.DayNumber + 1;
        if (edges && coveredTo.AddDays(length) <= end)
        {
            yield return $"Lipsește factura de comision {name} pentru perioada de după {AccountingJson.Date(coveredTo)}.";
        }
    }

    /// <summary>Salvează rezultatul (un singur rând pe PFA și lună).</summary>
    public static async Task SaveAsync(IApplicationDbContext db, Guid pfaId, string period, PreCheckResult result, CancellationToken cancellationToken)
    {
        PfaMonthCheck? check = await db.PfaMonthChecks.SingleOrDefaultAsync(c => c.PfaRegistrationId == pfaId && c.Period == period, cancellationToken);
        if (check is null)
        {
            check = new PfaMonthCheck { Id = Guid.NewGuid(), PfaRegistrationId = pfaId, Period = period };
            db.PfaMonthChecks.Add(check);
        }

        check.Status = result.Status;
        check.ReasonsJson = AccountingJson.Serialize(result.Reasons);
        check.CheckedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Rulează și salvează pre-check-ul unui PFA.</summary>
    public static async Task<PreCheckResult?> RunAsync(IApplicationDbContext db, Guid pfaId, string period, AccountingOptions options, CancellationToken cancellationToken)
    {
        List<ScopePfa> scope = await AccountingScope.InPeriodAsync(db, period, cancellationToken, pfaId);
        if (scope.Count == 0)
        {
            return null;
        }

        MonthData data = await MonthData.LoadAsync(db, period, [pfaId], cancellationToken);
        PreCheckResult result = Evaluate(scope[0], data, TaxEngineSettings.From(options));
        await SaveAsync(db, pfaId, period, result, cancellationToken);
        return result;
    }

    /// <summary>
    /// După o schimbare pe documente (confirmare, editare, citire), statusul lunii se reface — dar
    /// doar dacă luna a fost deja procesată; altfel rămâne „neprocesat”.
    /// </summary>
    public static async Task RefreshIfProcessedAsync(IApplicationDbContext db, Guid pfaId, string period, AccountingOptions options, CancellationToken cancellationToken)
    {
        bool processed = await db.PfaMonthChecks.AnyAsync(c => c.PfaRegistrationId == pfaId && c.Period == period, cancellationToken);
        if (processed)
        {
            await RunAsync(db, pfaId, period, options, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
