using Application.Abstractions.Data;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Application.Accounting.Tax;

/// <summary>Cursul de schimb aplicabil unei date (spec contabilitate B2).</summary>
public interface IExchangeRateProvider
{
    /// <summary>Cursul după regula zilei din configurare (DE CONFIRMAT); <c>null</c> dacă lipsește.</summary>
    Task<ExchangeRate?> GetAsync(string currency, DateOnly date, CancellationToken cancellationToken);
}

/// <summary>Cursurile importate din BNR, din tabelul <c>exchange_rates</c>.</summary>
internal sealed class ExchangeRateProvider(IApplicationDbContext db, IOptions<AccountingOptions> options) : IExchangeRateProvider
{
    public async Task<ExchangeRate?> GetAsync(string currency, DateOnly date, CancellationToken cancellationToken)
    {
        // O fereastră de două săptămâni acoperă weekendurile și sărbătorile, fără să citească tot istoricul.
        DateOnly from = date.AddDays(-14);
        string code = currency.ToUpperInvariant();
        List<ExchangeRate> candidates = await db.ExchangeRates
            .AsNoTracking()
            .Where(rate => rate.Currency == code && rate.Date >= from && rate.Date <= date)
            .ToListAsync(cancellationToken);
        return MonthlyTaxEngine.PickRate(candidates, currency, date, options.Value.ExchangeRateDate);
    }
}
