using Application.Abstractions.Messaging;
using Application.Payments.GrantDashboardAccess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Background job that runs periodically to check if it's Monday 15:00 Romania time.
/// If it is, it grants dashboard access to all users who have paid.
/// </summary>
internal sealed class MondayAccessGrantJob(
    IServiceScopeFactory scopeFactory,
    ILogger<MondayAccessGrantJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MondayAccessGrantJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (IsMondayFifteenOClockRomania())
                {
                    logger.LogInformation("It's Monday 15:00 Romania time. Granting dashboard access...");
                    
                    using IServiceScope scope = scopeFactory.CreateScope();
                    ICommandHandler<GrantDashboardAccessCommand> handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<GrantDashboardAccessCommand>>();

                    await handler.Handle(new GrantDashboardAccessCommand(), stoppingToken);
                    
                    logger.LogInformation("Dashboard access granted successfully.");

                    // Wait an hour so we don't trigger multiple times within the same 15:00-15:01 window
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred in MondayAccessGrantJob.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private static bool IsMondayFifteenOClockRomania()
    {
        var romaniaZone = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime nowRomania = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, romaniaZone);

        return nowRomania.DayOfWeek == DayOfWeek.Monday && 
               nowRomania.Hour == 15 && 
               nowRomania.Minute == 0;
    }
}
