using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Handover;
using Application.Accounting.Months;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Rulează joburile lunii (spec contabilitate B3) și dosarul de predare (B8) din tabelul <c>background_jobs</c>, pe rând. Un
/// job rămas „în lucru” după un restart se reia de la capăt: procesarea, generarea și validarea
/// sunt idempotente (validarea ia doar versiunile rămase în <c>GENERATED</c>).
/// </summary>
internal sealed class AccountingJobRunner(IServiceScopeFactory scopeFactory, ILogger<AccountingJobRunner> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private static readonly BackgroundJobType[] MonthJobs =
    [
        BackgroundJobType.ProcessPeriod,
        BackgroundJobType.GenerateDeclarations,
        BackgroundJobType.ValidateDeclarations,
        BackgroundJobType.HandoverPackage,
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueInterruptedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                IApplicationDbContext db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var next = await db.BackgroundJobs
                    .Where(j => j.Status == BackgroundJobStatus.Queued && MonthJobs.Contains(j.Type))
                    .OrderBy(j => j.CreatedAtUtc)
                    .Select(j => new { j.Id, j.Type })
                    .FirstOrDefaultAsync(stoppingToken);

                if (next is not null)
                {
                    Guid jobId = next.Id;
                    // Dosarul de predare are rulatorul lui; restul sunt joburile lunii.
                    Result result = next.Type == BackgroundJobType.HandoverPackage
                        ? await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunHandoverPackageCommand>>().Handle(new RunHandoverPackageCommand(jobId), stoppingToken)
                        : await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunMonthJobCommand>>().Handle(new RunMonthJobCommand(jobId), stoppingToken);
                    if (result.IsFailure)
                    {
                        logger.LogWarning("Jobul de contabilitate {JobId} a eșuat: {Error}", jobId, result.Error.Description);
                    }

                    continue;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Rulatorul joburilor de contabilitate a eșuat.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task RequeueInterruptedAsync(CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IApplicationDbContext db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            List<BackgroundJob> running = await db.BackgroundJobs.Where(j => j.Status == BackgroundJobStatus.Running).ToListAsync(stoppingToken);
            running.ForEach(job => job.Status = BackgroundJobStatus.Queued);
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Joburile de contabilitate întrerupte nu au putut fi reluate.");
        }
    }
}
