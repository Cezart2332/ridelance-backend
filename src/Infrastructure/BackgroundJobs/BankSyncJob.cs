using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Application.Banking;
using Domain.Banking;
using Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Sincronizează periodic tranzacțiile bancare (open banking).
/// Băncile limitează accesul PSD2 la ~4 apeluri/zi per cont per endpoint, deci
/// fiecare cont e sincronizat cel mult o dată la 12h (max 2×/zi), cu backoff
/// persistat la 429. Tot aici se detectează expirarea consimțământului.
/// </summary>
internal sealed class BankSyncJob(
    IServiceScopeFactory scopeFactory,
    ILogger<BankSyncJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinTimeBetweenAccountSyncs = TimeSpan.FromHours(12);
    private static readonly TimeSpan DefaultRateLimitBackoff = TimeSpan.FromHours(12);
    private static readonly TimeSpan ExpiryWarningWindow = TimeSpan.FromDays(7);
    private const int MaxConsecutiveFailuresBeforeError = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BankSyncJob background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred in BankSyncJob during periodic sync execution.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task RunPassAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IApplicationDbContext context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        IBankDataProvider provider = scope.ServiceProvider.GetRequiredService<IBankDataProvider>();
        BankAccountSyncService syncService = scope.ServiceProvider.GetRequiredService<BankAccountSyncService>();

        if (!provider.IsConfigured)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;

        List<BankConnection> connections = await context.BankConnections
            .Include(bc => bc.Accounts)
            .Where(bc => bc.Status == BankConnectionStatus.Linked)
            .ToListAsync(ct);

        foreach (BankConnection connection in connections)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            // Consimțământ expirat: marcăm și notificăm, fără apeluri către provider.
            if (connection.ConsentExpiresAtUtc is DateTime expires && expires <= now)
            {
                connection.Status = BankConnectionStatus.Expired;
                AddReconnectNotification(
                    context,
                    connection,
                    $"Conexiunea cu {connection.InstitutionName} a expirat. Reconectează banca din dashboard pentru a continua sincronizarea tranzacțiilor.");
                await context.SaveChangesAsync(ct);
                continue;
            }

            // Avertizare cu 7 zile înainte de expirare (o singură dată per consimțământ).
            if (connection.ConsentExpiresAtUtc is DateTime soon &&
                soon <= now + ExpiryWarningWindow &&
                connection.ExpiryNotifiedAtUtc is null)
            {
                connection.ExpiryNotifiedAtUtc = now;
                AddReconnectNotification(
                    context,
                    connection,
                    $"Conexiunea cu {connection.InstitutionName} expiră pe {soon:dd.MM.yyyy}. Reconectează banca din dashboard pentru a nu întrerupe sincronizarea.");
                await context.SaveChangesAsync(ct);
            }

            bool hadFailure = false;

            foreach (Domain.Banking.BankAccount account in connection.Accounts.Where(a => a.IsActive))
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                bool dueForSync =
                    (account.RateLimitedUntilUtc is null || account.RateLimitedUntilUtc < now) &&
                    (account.LastTransactionsSyncedAtUtc is null ||
                     account.LastTransactionsSyncedAtUtc < now - MinTimeBetweenAccountSyncs);

                if (!dueForSync)
                {
                    continue;
                }

                try
                {
                    await syncService.SyncAccountAsync(account, connection, ct);
                    account.RateLimitedUntilUtc = null;
                    logger.LogInformation(
                        "Synced bank account {AccountId} for user {UserId}.",
                        account.Id,
                        connection.UserId);
                }
                catch (BankDataRateLimitException ex)
                {
                    account.RateLimitedUntilUtc = now.Add(ex.RetryAfter ?? DefaultRateLimitBackoff);
                    logger.LogWarning(
                        ex,
                        "Rate limited syncing bank account {AccountId}; backing off until {Until}.",
                        account.Id,
                        account.RateLimitedUntilUtc);
                }
                catch (BankDataConsentExpiredException)
                {
                    connection.Status = BankConnectionStatus.Expired;
                    AddReconnectNotification(
                        context,
                        connection,
                        $"Conexiunea cu {connection.InstitutionName} a expirat. Reconectează banca din dashboard pentru a continua sincronizarea tranzacțiilor.");
                    break;
                }
                catch (Exception ex)
                {
                    hadFailure = true;
                    connection.ErrorMessage = ex.Message;
                    logger.LogError(
                        ex,
                        "Failed to sync bank account {AccountId} for user {UserId}.",
                        account.Id,
                        connection.UserId);
                }

                // Pauză scurtă între conturi ca să nu lovim providerul în rafală.
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }

            if (hadFailure)
            {
                connection.ConsecutiveFailures++;
                if (connection.ConsecutiveFailures >= MaxConsecutiveFailuresBeforeError)
                {
                    connection.Status = BankConnectionStatus.Error;
                }
            }
            else if (connection.Status == BankConnectionStatus.Linked)
            {
                connection.ConsecutiveFailures = 0;
                connection.ErrorMessage = null;
            }

            await context.SaveChangesAsync(ct);
        }
    }

    private static void AddReconnectNotification(
        IApplicationDbContext context,
        BankConnection connection,
        string text)
    {
        context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = connection.UserId,
            Text = text,
            Type = NotificationTypes.BankConnection,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow,
        });
    }
}
