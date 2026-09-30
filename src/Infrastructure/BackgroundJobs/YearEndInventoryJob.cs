using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Inventory;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Inventarierea de la 31.12 (spec registre §5 pas 1): pe 31 decembrie și în ianuarie, fiecare PFA cu
/// colaborarea contabilă activă primește inventarierea precompletată, dacă nu o are deja. Rulează la
/// câteva ore; e idempotentă prin cheia (PFA, dată, motiv).
/// </summary>
internal sealed class YearEndInventoryJob(IServiceScopeFactory scopeFactory, IDateTimeProvider clock, ILogger<YearEndInventoryJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                int created = await RunAsync(
                    scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
                    scope.ServiceProvider.GetRequiredService<ICommandHandler<StartInventoryCountCommand, InventoryCountDto>>(),
                    DateOnly.FromDateTime(clock.UtcNow),
                    stoppingToken);
                if (created > 0)
                {
                    logger.LogInformation("Inventarieri de sfârșit de an create: {Count}.", created);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Crearea inventarierilor de sfârșit de an a eșuat.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Inventarierile lipsă pentru 31.12 al anului care se încheie (sau tocmai s-a încheiat).</summary>
    internal static async Task<int> RunAsync(
        IApplicationDbContext db, ICommandHandler<StartInventoryCountCommand, InventoryCountDto> start, DateOnly today, CancellationToken cancellationToken)
    {
        if (today is not ({ Month: 12, Day: 31 } or { Month: 1 }))
        {
            return 0;
        }

        var date = new DateOnly(today.Month == 12 ? today.Year : today.Year - 1, 12, 31);
        List<Guid> pfas = await db.PfaAccountingEngagements.AsNoTracking()
            .Where(e => e.Status == EngagementStatus.Active && e.StartDate <= date &&
                        !db.InventoryCounts.Any(c => c.PfaRegistrationId == e.PfaRegistrationId && c.Date == date && c.Reason == InventoryReason.YearEnd))
            .Select(e => e.PfaRegistrationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        int created = 0;
        foreach (Guid pfaId in pfas)
        {
            Result<InventoryCountDto> result = await start.Handle(new StartInventoryCountCommand(pfaId, date, InventoryReason.YearEnd), cancellationToken);
            created += result.IsSuccess ? 1 : 0;
        }

        return created;
    }
}
