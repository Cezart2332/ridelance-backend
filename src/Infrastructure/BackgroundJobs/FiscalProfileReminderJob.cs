using Application.Abstractions.Messaging;
using Application.FiscalProfiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Rulează zilnic la 10:00, ora României: reamintirea săptămânală pentru profilul fiscal
/// necompletat și sarcina de apel de la ziua 30. Idempotent — unicitatea pe (PFA, an, săptămână)
/// și pe (PFA, an, motiv) ține chiar dacă rulează de mai multe ori în aceeași zi.
/// </summary>
internal sealed class FiscalProfileReminderJob(
    IServiceScopeFactory scopeFactory,
    ILogger<FiscalProfileReminderJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeZoneInfo Romania = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTime nowRomania = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Romania);
                if (nowRomania is { Hour: 10, Minute: 0 })
                {
                    using IServiceScope scope = scopeFactory.CreateScope();
                    ICommandHandler<RunFiscalProfileRemindersCommand, FiscalProfileRemindersRun> handler = scope.ServiceProvider
                        .GetRequiredService<ICommandHandler<RunFiscalProfileRemindersCommand, FiscalProfileRemindersRun>>();

                    Result<FiscalProfileRemindersRun> result = await handler.Handle(new RunFiscalProfileRemindersCommand(), stoppingToken);
                    if (result.IsFailure)
                    {
                        logger.LogWarning("Reamintirile de profil fiscal au eșuat: {Error}", result.Error);
                    }

                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Eroare în FiscalProfileReminderJob.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
