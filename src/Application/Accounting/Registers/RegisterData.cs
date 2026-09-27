using System.Globalization;
using Application.Abstractions.Data;
using Application.Accounting.Ledger;
using Domain.Accounting;
using Application.Accounting.Pfas;
using Microsoft.EntityFrameworkCore;

namespace Application.Accounting.Registers;

/// <summary>O înregistrare din ledger, în lei, cu mențiunea valutei (OMFP 170/2015, cap. III pct. 18).</summary>
internal sealed record RegisterEntry(LedgerEntry Entry, decimal AmountLei, decimal? DeductibleLei, string? CurrencyNote);

/// <summary>
/// Registrele sunt proiecții din ledger (spec contabilitate B7), nu documente stocate. Aici se
/// alege ce intră și cum se trece în lei.
/// </summary>
internal static class RegisterData
{
    public static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>Numele PFA-ului și CUI-ul, pentru antetul registrelor.</summary>
    public static async Task<(string Name, string Cui)?> PfaAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken)
    {
        var pfa = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.Id == pfaId)
            .Select(p => new { p.LegalName, p.HolderName, p.FullName, p.Cui, p.User.FirstName, p.User.LastName })
            .SingleOrDefaultAsync(cancellationToken);
        return pfa is null ? null : (PfaNames.Of(pfa.LegalName, pfa.HolderName, pfa.FullName, pfa.FirstName, pfa.LastName), pfa.Cui ?? string.Empty);
    }

    /// <summary>
    /// Înregistrările din interval care contează în registre: fără importurile căzute într-o lună
    /// închisă (intră doar după corecție, B6/B8), în ordine cronologică, cu sumele în lei. O sumă în
    /// valută se trece la cursul BNR din ultima zi bancară anterioară operațiunii.
    /// </summary>
    public static async Task<List<RegisterEntry>> EntriesAsync(IApplicationDbContext db, Guid pfaId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        List<LedgerEntry> entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfaId && e.Date >= from && e.Date <= to && !e.ClosedPeriodFlag)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        List<string> currencies = [.. entries.Select(e => e.Currency).Where(c => c != "RON").Distinct()];
        DateOnly ratesFrom = from.AddDays(-15);
        List<ExchangeRate> rates = currencies.Count == 0
            ? []
            : await db.ExchangeRates.AsNoTracking()
                .Where(r => currencies.Contains(r.Currency) && r.Date >= ratesFrom && r.Date < to)
                .ToListAsync(cancellationToken);

        return [.. entries.Select(entry => Convert(entry, rates))];
    }

    /// <summary>
    /// O mișcare de bani (încasare sau plată), deci un rând RJIP. Venitul brut și comisionul luate
    /// dintr-un raport de platformă (<c>GrossReport</c>) nu sunt bani mișcați: nu intră în RJIP.
    /// </summary>
    public static bool IsCashMovement(LedgerEntry entry) => !(entry.BankTransactionId is null && entry.PlatformDocumentId is not null);

    public static string Amount(decimal value) => value.ToString("#,##0.00", Ro);

    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static RegisterEntry Convert(LedgerEntry entry, List<ExchangeRate> rates)
    {
        if (entry.Currency == "RON")
        {
            return new RegisterEntry(entry, entry.Amount, entry.DeductibleAmount, null);
        }

        ExchangeRate? rate = rates
            .Where(r => r.Currency == entry.Currency && r.Date < entry.Date)
            .OrderByDescending(r => r.Date)
            .FirstOrDefault();
        if (rate is null)
        {
            return new RegisterEntry(entry, entry.Amount, entry.DeductibleAmount, $"{Amount(Math.Abs(entry.Amount))} {entry.Currency}, curs BNR lipsă — de verificat");
        }

        decimal lei = Math.Round(entry.Amount * rate.Rate, 2, MidpointRounding.AwayFromZero);
        decimal? deductible = entry.DeductibleAmount is { } value ? Math.Round(value * rate.Rate, 2, MidpointRounding.AwayFromZero) : null;
        return new RegisterEntry(entry, lei, deductible, $"{Amount(Math.Abs(entry.Amount))} {entry.Currency} × {rate.Rate.ToString("0.####", Ro)} (BNR {Date(rate.Date)})");
    }

    public static string PeriodOf(DateOnly date) => LedgerSupport.PeriodOf(date);
}
