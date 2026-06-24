using Application.Abstractions.Data;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

internal sealed class PfaIncomePeriodInitializationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<PfaIncomePeriodInitializationJob> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PfaIncomePeriodInitializationJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureCurrentMonthRowsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in PfaIncomePeriodInitializationJob.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task EnsureCurrentMonthRowsAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IApplicationDbContext context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        DateTime now = DateTime.UtcNow;
        List<PfaRegistration> pfas = await context.PfaRegistrations.ToListAsync(ct);
        foreach (PfaRegistration pfa in pfas)
        {
            bool exists = await context.PfaMonthlyIncomes.AnyAsync(
                i => i.PfaRegistrationId == pfa.Id && i.Year == now.Year && i.Month == now.Month,
                ct);

            if (!exists)
            {
                context.PfaMonthlyIncomes.Add(new PfaMonthlyIncome
                {
                    Id = Guid.NewGuid(),
                    PfaRegistrationId = pfa.Id,
                    Year = now.Year,
                    Month = now.Month,
                    UpdatedAtUtc = DateTime.UtcNow,
                    UpdatedByUserId = pfa.UserId
                });
            }
        }

        await context.SaveChangesAsync(ct);
    }
}
