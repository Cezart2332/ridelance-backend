using System.Globalization;
using Application.Abstractions.Messaging;
using Application.Accounting.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Importul zilnic al ledger-ului (spec contabilitate B6): bancă, Uber/Bolt și Oblio pentru toate
/// PFA-urile active în luna curentă. Importatorii sunt idempotenți, deci o rulare în plus nu strică.
/// </summary>
internal sealed class LedgerImportJob(IServiceScopeFactory scopeFactory, ILogger<LedgerImportJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Pornirea aplicației nu așteaptă importul.</summary>
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartDelay, stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<Guid> pfas;
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            Result<IReadOnlyList<Guid>> listed = await scope.ServiceProvider
                .GetRequiredService<IQueryHandler<ListLedgerImportPfasQuery, IReadOnlyList<Guid>>>()
                .Handle(new ListLedgerImportPfasQuery(DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture)), stoppingToken);
            pfas = listed.IsSuccess ? listed.Value : [];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Importul ledger-ului: lista PFA-urilor nu a putut fi citită.");
            return;
        }

        foreach (Guid pfaId in pfas)
        {
            try
            {
                // Un scope per PFA: un import eșuat nu lasă modificări pe jumătate în contextul următorului.
                using IServiceScope scope = scopeFactory.CreateScope();
                Result<IReadOnlyList<LedgerImportResult>> result = await scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<RunLedgerImportCommand, IReadOnlyList<LedgerImportResult>>>()
                    .Handle(new RunLedgerImportCommand(pfaId), stoppingToken);
                if (result.IsFailure)
                {
                    logger.LogInformation("Importul ledger-ului pentru {PfaId} a fost sărit: {Error}", pfaId, result.Error.Description);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Importul ledger-ului pentru {PfaId} a eșuat.", pfaId);
            }
        }
    }
}
