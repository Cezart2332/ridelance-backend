using Application.Abstractions.Messaging;
using Application.Accounting.Anaf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// e-Factura în fundal: tokenul ANAF se reînnoiește înainte să expire, iar clienții conectați se
/// sincronizează de două ori pe zi. Fără conexiune ANAF, jobul nu face nimic.
/// </summary>
internal sealed class EFacturaSyncJob(IServiceScopeFactory scopeFactory, ILogger<EFacturaSyncJob> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "EFacturaSyncJob: pasul a eșuat.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ICommandHandler<RunEFacturaSyncPassCommand, int> pass = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunEFacturaSyncPassCommand, int>>();
        Result<int> result = await pass.Handle(new RunEFacturaSyncPassCommand(), cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("EFacturaSyncJob: {Error}", result.Error.Description);
        }
    }
}
