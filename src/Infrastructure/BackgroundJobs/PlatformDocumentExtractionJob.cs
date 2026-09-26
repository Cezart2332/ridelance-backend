using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Documents;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Coada de extracție a documentelor Uber/Bolt (spec contabilitate B1): ține documentele
/// <c>EXTRACTING</c> în baza de date și le citește pe rând, ca upload-ul să răspundă imediat.
/// După un restart, documentele rămase în coadă se reiau singure.
/// </summary>
internal sealed class PlatformDocumentExtractionJob(
    IServiceScopeFactory scopeFactory,
    ILogger<PlatformDocumentExtractionJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                IApplicationDbContext db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                List<Guid> queued = await db.PlatformDocuments
                    .Where(d => d.Status == PlatformDocumentStatus.Extracting)
                    .OrderBy(d => d.UploadedAtUtc)
                    .Select(d => d.Id)
                    .Take(BatchSize)
                    .ToListAsync(stoppingToken);

                foreach (Guid id in queued)
                {
                    // Un scope pe document: o eroare la unul nu lasă context murdar pentru următorul.
                    using IServiceScope documentScope = scopeFactory.CreateScope();
                    ICommandHandler<RunPlatformDocumentExtractionCommand> handler =
                        documentScope.ServiceProvider.GetRequiredService<ICommandHandler<RunPlatformDocumentExtractionCommand>>();
                    Result result = await handler.Handle(new RunPlatformDocumentExtractionCommand(id), stoppingToken);
                    if (result.IsFailure)
                    {
                        logger.LogWarning("Extracția documentului {DocumentId} a eșuat: {Error}", id, result.Error.Description);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Coada de extracție a documentelor de platformă a eșuat.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
