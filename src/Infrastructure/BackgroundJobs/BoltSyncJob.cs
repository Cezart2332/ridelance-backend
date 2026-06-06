using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Domain.Bolt;
using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0007, IDE0008

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Periodically syncs Bolt orders for all active integrations in real-time.
/// Runs every 5 minutes.
/// </summary>
internal sealed class BoltSyncJob(
    IServiceScopeFactory scopeFactory,
    ILogger<BoltSyncJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BoltSyncJob background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAllIntegrationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred in BoltSyncJob during periodic sync execution.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task SyncAllIntegrationsAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IApplicationDbContext context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        IBoltService boltService = scope.ServiceProvider.GetRequiredService<IBoltService>();

        // Get all connected active integrations
        List<BoltIntegration> integrations = await context.BoltIntegrations
            .Where(bi => bi.IsConnected)
            .ToListAsync(ct);

        if (integrations.Count == 0)
        {
            return;
        }

        logger.LogInformation("Found {Count} active Bolt integrations to sync.", integrations.Count);

        foreach (BoltIntegration integration in integrations)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                logger.LogInformation("Syncing Bolt orders for user: {UserId}, company: {CompanyId}", integration.UserId, integration.CompanyId);

                // Sync orders for the last 2 days to capture new/updated orders
                var end = DateTime.UtcNow;
                var start = end.AddDays(-2); // Fetch orders from the last 2 days to account for late/delayed entries

                List<BoltOrder> orders = await boltService.FetchOrdersAsync(integration, start, end, ct);

                if (orders.Count > 0)
                {
                    var existingRefs = orders.Select(o => o.OrderReference).ToList();
                    
                    var ordersToRemove = await context.BoltOrders
                        .Where(o => o.UserId == integration.UserId && existingRefs.Contains(o.OrderReference))
                        .ToListAsync(ct);

                    context.BoltOrders.RemoveRange(ordersToRemove);

                    foreach (BoltOrder order in orders)
                    {
                        context.BoltOrders.Add(order);
                    }
                }

                integration.IsConnected = true;
                integration.ErrorMessage = null;
                integration.LastFetchedAtUtc = DateTime.UtcNow;

                await context.SaveChangesAsync(ct);
                
                logger.LogInformation("Successfully synced {Count} orders for user: {UserId}", orders.Count, integration.UserId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync Bolt integration for user {UserId}", integration.UserId);
                
                // Track failure on integration entity
                integration.IsConnected = false;
                integration.ErrorMessage = ex.Message;
                await context.SaveChangesAsync(ct);
            }
        }
    }
}
