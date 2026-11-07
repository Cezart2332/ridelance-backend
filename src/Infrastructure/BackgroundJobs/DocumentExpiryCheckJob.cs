using Application.Abstractions.Messaging;
using Application.Notifications.DocumentExpiry;
using Application.PfaRegistrations.MonthlyIncome;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// O dată pe zi, după 08:00 (ora României): notificări în aplicație și push pentru documentele care
/// expiră. Rula doar dacă bucla nimerea exact minutul 08:00, iar un restart sau o deriva de câteva
/// secunde sărea ziua cu totul. Acum verifică la câteva minute și rulează prima dată după 08:00;
/// handlerul e idempotent, deci o rulare în plus nu dublează nimic.
/// </summary>
internal sealed class DocumentExpiryCheckJob(
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentExpiryCheckJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private const int RunFromHour = 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DocumentExpiryCheckJob started.");
        DateOnly? lastRun = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTime nowRomania = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow, PfaMonthlyIncomeRecalculator.GetRomaniaTimeZone());
                var today = DateOnly.FromDateTime(nowRomania);

                if (nowRomania.Hour >= RunFromHour && lastRun != today)
                {
                    logger.LogInformation("Running daily document expiry check...");

                    using IServiceScope scope = scopeFactory.CreateScope();
                    ICommandHandler<CheckDocumentExpiryCommand> handler =
                        scope.ServiceProvider.GetRequiredService<ICommandHandler<CheckDocumentExpiryCommand>>();

                    Result result = await handler.Handle(new CheckDocumentExpiryCommand(), stoppingToken);

                    if (result.IsFailure)
                    {
                        logger.LogWarning("Document expiry check returned failure: {Error}", result.Error);
                    }
                    else
                    {
                        lastRun = today;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in DocumentExpiryCheckJob.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
