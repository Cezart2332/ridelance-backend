using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Documents.AiVerification;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Procesează în fundal coada de documente aflate în așteptarea prevalidării AI,
/// ca să nu blocheze upload-ul. Reia automat documentele rămase „Processing”
/// după un restart și lasă un interval de 2 minute între reîncercări.
/// </summary>
internal sealed class DocumentAiVerificationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentAiVerificationJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(2);
    private const int BatchSize = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DocumentAiVerificationJob started.");

        try
        {
            // Recuperare după un eventual crash: documentele rămase „Processing” revin în coadă.
            using IServiceScope startupScope = scopeFactory.CreateScope();
            IApplicationDbContext startupDb = startupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            await startupDb.Documents
                .Where(d => d.AiStatus == DocumentAiStatus.Processing)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(d => d.AiStatus, DocumentAiStatus.Queued),
                    stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to requeue in-flight AI verifications on startup.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                IApplicationDbContext db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                DateTime retryCutoff = DateTime.UtcNow - RetryDelay;
                List<Guid> documentIds = await db.Documents
                    .Where(d => d.AiStatus == DocumentAiStatus.Queued &&
                                (d.AiProcessedAtUtc == null || d.AiProcessedAtUtc < retryCutoff))
                    .OrderBy(d => d.UploadedAtUtc)
                    .Select(d => d.Id)
                    .Take(BatchSize)
                    .ToListAsync(stoppingToken);

                foreach (Guid documentId in documentIds)
                {
                    ICommandHandler<RunDocumentAiVerificationCommand> handler =
                        scope.ServiceProvider.GetRequiredService<ICommandHandler<RunDocumentAiVerificationCommand>>();

                    Result result = await handler.Handle(
                        new RunDocumentAiVerificationCommand(documentId),
                        stoppingToken);

                    if (result.IsFailure)
                    {
                        logger.LogWarning(
                            "AI verification failed for document {DocumentId}: {Error}",
                            documentId,
                            result.Error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown in progress.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in DocumentAiVerificationJob.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
