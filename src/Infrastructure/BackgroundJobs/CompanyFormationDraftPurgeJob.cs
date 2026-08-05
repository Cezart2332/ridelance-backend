using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.CompanyFormation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Rulează zilnic la 03:00, ora României: șterge ciornele de dosar de înființare abandonate de
/// peste 90 de zile (minimizare GDPR, spec §8).
/// </summary>
internal sealed class CompanyFormationDraftPurgeJob(
    IServiceScopeFactory scopeFactory,
    ILogger<CompanyFormationDraftPurgeJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (IsThreeAmRomania())
                {
                    using IServiceScope scope = scopeFactory.CreateScope();
                    ICommandHandler<PurgeAbandonedCompanyFormationDraftsCommand, int> handler =
                        scope.ServiceProvider
                            .GetRequiredService<ICommandHandler<PurgeAbandonedCompanyFormationDraftsCommand, int>>();

                    Result<int> result = await handler.Handle(
                        new PurgeAbandonedCompanyFormationDraftsCommand(), stoppingToken);

                    if (result.IsFailure)
                    {
                        logger.LogWarning("Ștergerea ciornelor a eșuat: {Error}", result.Error);
                    }
                    else if (result.Value > 0)
                    {
                        logger.LogInformation("Am șters {Count} ciorne de dosar abandonate.", result.Value);
                    }

                    // O oră de pauză, ca să nu se retrimită în aceeași oră.
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Eroare în CompanyFormationDraftPurgeJob.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private static bool IsThreeAmRomania()
    {
        var romania = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime nowRomania = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, romania);

        return nowRomania.Hour == 3 && nowRomania.Minute == 0;
    }
}
