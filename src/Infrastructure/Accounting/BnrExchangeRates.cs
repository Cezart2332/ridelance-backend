using System.Globalization;
using System.Xml.Linq;
using Application.Accounting;
using Domain.Accounting;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Accounting;

/// <summary>Un curs de referință BNR: lei pentru o unitate de valută.</summary>
public sealed record BnrRate(DateOnly Date, string Currency, decimal Rate);

/// <summary>
/// Importul cursurilor de referință BNR (spec contabilitate B2). Sursa e publică, fără cheie:
/// fișierul zilnic și, pentru completarea istoricului, cel al anului.
/// </summary>
internal sealed class BnrExchangeRateImporter(HttpClient httpClient, ApplicationDbContext db, ILogger<BnrExchangeRateImporter> logger)
{
    public const string Source = "BNR";
    private static readonly XNamespace Bnr = "http://www.bnr.ro/xsd";

    public static readonly Uri DailyUrl = new("https://www.bnr.ro/nbrfxrates.xml");

    public static Uri YearUrl(int year) => new($"https://www.bnr.ro/files/xml/years/nbrfxrates{year}.xml");

    /// <summary>Descarcă, parsează și adaugă cursurile care lipsesc. Întoarce câte au intrat.</summary>
    public async Task<int> ImportAsync(Uri url, IReadOnlyCollection<string> currencies, CancellationToken cancellationToken)
    {
        string xml = await httpClient.GetStringAsync(url, cancellationToken);
        List<BnrRate> rates = [.. Parse(xml).Where(rate => currencies.Contains(rate.Currency, StringComparer.OrdinalIgnoreCase))];
        if (rates.Count == 0)
        {
            return 0;
        }

        DateOnly from = rates.Min(rate => rate.Date);
        HashSet<(DateOnly, string)> existing = [.. (await db.ExchangeRates
                .Where(rate => rate.Source == Source && rate.Date >= from)
                .Select(rate => new { rate.Date, rate.Currency })
                .ToListAsync(cancellationToken))
            .Select(rate => (rate.Date, rate.Currency))];

        List<ExchangeRate> added = [.. rates
            .Where(rate => !existing.Contains((rate.Date, rate.Currency)))
            .Select(rate => new ExchangeRate { Id = Guid.NewGuid(), Currency = rate.Currency, Date = rate.Date, Rate = rate.Rate, Source = Source })];
        db.ExchangeRates.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Import BNR {Url}: {Count} cursuri noi.", url, added.Count);
        return added.Count;
    }

    /// <summary>
    /// XML-ul BNR: <c>&lt;Cube date="…"&gt;&lt;Rate currency="EUR"&gt;4.9760&lt;/Rate&gt;</c>, cu
    /// <c>multiplier</c> pentru valutele cotate la 100 de unități.
    /// </summary>
    public static IReadOnlyList<BnrRate> Parse(string xml)
    {
        var document = XDocument.Parse(xml);
        var rates = new List<BnrRate>();
        foreach (XElement cube in document.Descendants(Bnr + "Cube"))
        {
            var date = DateOnly.ParseExact((string)cube.Attribute("date")!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (XElement rate in cube.Elements(Bnr + "Rate"))
            {
                decimal value = decimal.Parse(rate.Value, NumberStyles.Number, CultureInfo.InvariantCulture);
                decimal multiplier = rate.Attribute("multiplier") is { } attribute
                    ? decimal.Parse(attribute.Value, NumberStyles.Number, CultureInfo.InvariantCulture)
                    : 1;
                rates.Add(new BnrRate(date, ((string)rate.Attribute("currency")!).ToUpperInvariant(), value / multiplier));
            }
        }

        return rates;
    }
}

/// <summary>Importul zilnic BNR; la pornire completează anul curent.</summary>
internal sealed class BnrExchangeRateImportJob(
    IServiceScopeFactory scopeFactory,
    IOptions<AccountingOptions> options,
    ILogger<BnrExchangeRateImportJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool backfilled = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                BnrExchangeRateImporter importer = scope.ServiceProvider.GetRequiredService<BnrExchangeRateImporter>();
                IReadOnlyCollection<string> currencies = [.. options.Value.AllowedCurrencies.Where(currency => !currency.Equals("RON", StringComparison.OrdinalIgnoreCase))];
                if (!backfilled)
                {
                    await importer.ImportAsync(BnrExchangeRateImporter.YearUrl(DateTime.UtcNow.Year), currencies, stoppingToken);
                    backfilled = true;
                }

                await importer.ImportAsync(BnrExchangeRateImporter.DailyUrl, currencies, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Importul cursurilor BNR a eșuat; se reîncearcă la următorul interval.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
