using Application.Abstractions.Messaging;
using Application.FiscalEstimates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Coada de recalculare a taxelor estimate: la 15 secunde ia PFA-urile cu profil fiscal completat
/// care n-au rulare, au una expirată de peste 30 de secunde (debounce), sau una din altă zi —
/// proiecția se schimbă odată cu săptămânile rămase din an.
/// </summary>
internal sealed class FiscalEstimateRecalculationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<FiscalEstimateRecalculationJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                ICommandHandler<ProcessFiscalEstimateQueueCommand, int> handler = scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<ProcessFiscalEstimateQueueCommand, int>>();

                Result<int> result = await handler.Handle(new ProcessFiscalEstimateQueueCommand(), stoppingToken);
                if (result.IsSuccess && result.Value > 0)
                {
                    logger.LogInformation("Taxe estimate recalculate pentru {Count} PFA.", result.Value);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Eroare în FiscalEstimateRecalculationJob.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
