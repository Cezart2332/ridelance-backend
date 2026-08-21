using Application.Abstractions.Data;
using Application.Cars.Scoring;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Recalculează nocturn scorul anunțurilor (spec §7.3).
/// </summary>
/// <remarks>
/// Restul criteriilor se recalculează la evenimentul care le schimbă. Prospețimea nu are un
/// eveniment: un anunț nu „se învechește" printr-o acțiune, ci prin trecerea timpului, deci
/// singura cale ca multiplicatorul să scadă la 8 și la 31 de zile e o trecere periodică.
///
/// Rulează în loturi și salvează pe lot: un marketplace mare nu are voie să țină o singură
/// tranzacție deschisă cât durează toată recalcularea.
/// </remarks>
internal sealed class ListingFreshnessScoreJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ListingFreshnessScoreJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Prima rulare așteaptă puțin, ca pornirea aplicației să nu concureze cu ea.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecalculateAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Un job de fundal nu are voie să omoare procesul.
            catch (Exception ex)
            {
                logger.LogError(ex, "Recalcularea nocturnă a scorurilor a eșuat.");
            }
#pragma warning restore CA1031

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RecalculateAllAsync(CancellationToken cancellationToken)
    {
        int processed = 0;
        Guid lastId = Guid.Empty;

        while (!cancellationToken.IsCancellationRequested)
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IApplicationDbContext context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            ListingScoreService scoreService = scope.ServiceProvider.GetRequiredService<ListingScoreService>();

            // Paginare pe Id, nu pe offset: scorurile se schimbă chiar în timpul parcurgerii, iar
            // un `Skip` ar sări peste anunțuri sau le-ar procesa de două ori.
            List<Car> batch = await context.Cars
                .Include(c => c.Images)
                .Where(c => c.Id.CompareTo(lastId) > 0)
                .OrderBy(c => c.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (Car car in batch)
            {
                await scoreService.RecalculateAsync(car, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);

            lastId = batch[^1].Id;
            processed += batch.Count;
        }

        logger.LogInformation("Scoruri recalculate pentru {Count} anunțuri.", processed);
    }
}
