using System.Globalization;
using Application.Abstractions.Data;
using Application.FiscalEstimates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Aduce în memorie plafoanele fiscale setate din admin: la pornire, apoi o dată pe minut.
///
/// Motorul de estimări citește parametrii sincron, fără bază de date; valorile din
/// <c>app_settings</c> ajung la el prin <see cref="TaxYearParametersProvider.ApplyOverrides"/>.
/// Reîmprospătarea periodică acoperă și o salvare (sau o resetare) făcută pe altă instanță.
/// </summary>
internal sealed class TaxParametersSyncJob(
    IServiceScopeFactory scopeFactory,
    TaxYearParametersProvider provider,
    ILogger<TaxParametersSyncJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fără sincronizare, calculul rămâne pe ultimele valori cunoscute — nu se oprește.
                logger.LogWarning(ex, "Plafoanele fiscale din admin nu s-au putut încărca.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IApplicationDbContext context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var rows = await context.AppSettings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith(TaxYearParametersProvider.SettingKeyPrefix))
            .Select(s => new { s.Key, s.ValueJson })
            .ToListAsync(cancellationToken);

        var overrides = new Dictionary<int, TaxYearParameters>();
        foreach (var row in rows)
        {
            string suffix = row.Key[TaxYearParametersProvider.SettingKeyPrefix.Length..];
            if (!int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int year))
            {
                continue;
            }

            TaxYearParameters? parameters = TaxYearParametersProvider.Deserialize(row.ValueJson);
            if (parameters is not null && parameters.TaxYear == year)
            {
                overrides[year] = parameters;
            }
        }

        provider.ApplyOverrides(overrides);
    }
}
