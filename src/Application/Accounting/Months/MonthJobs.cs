using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Application.Accounting.Tax;
using Domain.Accounting;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Months;

/// <summary><c>POST /accounting/periods/{period}/process | generate</c> — pornește un job.</summary>
public sealed record StartMonthJobCommand(BackgroundJobType Type, string Period) : ICommand<JobRef>;

internal sealed record MonthJobParameters(string Period);

internal sealed record MonthJobResults(List<JobResultItem> Results, List<JobResultItem> Errors);

internal sealed class StartMonthJobCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<StartMonthJobCommand, JobRef>
{
    public async Task<Result<JobRef>> Handle(StartMonthJobCommand command, CancellationToken cancellationToken)
    {
        if (!PlatformDocumentSupport.IsValidPeriod(command.Period))
        {
            return Result.Failure<JobRef>(AccountingErrors.InvalidPeriod);
        }

        var job = new BackgroundJob
        {
            Id = Guid.NewGuid(),
            Type = command.Type,
            Status = BackgroundJobStatus.Queued,
            ParametersJson = AccountingJson.Serialize(new MonthJobParameters(command.Period)),
            ResultJson = AccountingJson.Serialize(new MonthJobResults([], [])),
            CreatedByUserId = userContext.UserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return new JobRef(job.Id);
    }
}

/// <summary>Rulează un job din coadă (apelat de <c>AccountingJobRunner</c>).</summary>
public sealed record RunMonthJobCommand(Guid JobId) : ICommand;

/// <summary>
/// Joburile lunii (spec B3), PFA cu PFA, cu progresul scris în <see cref="BackgroundJob"/> după
/// fiecare unitate:
/// <list type="bullet">
/// <item><c>process</c>: citirea documentelor rămase în coadă, apoi pre-check-ul fiecărui PFA;</item>
/// <item><c>generate</c>: pentru PFA-urile gata fără declarații, calculul și versiunea 1.</item>
/// </list>
/// Validarea pe 3 niveluri e în B4.
/// </summary>
internal sealed class RunMonthJobCommandHandler(
    IApplicationDbContext db,
    ICommandHandler<RunPlatformDocumentExtractionCommand> extraction,
    IOptions<AccountingOptions> options)
    : ICommandHandler<RunMonthJobCommand>
{
    public async Task<Result> Handle(RunMonthJobCommand command, CancellationToken cancellationToken)
    {
        BackgroundJob? job = await db.BackgroundJobs.SingleOrDefaultAsync(j => j.Id == command.JobId, cancellationToken);
        if (job is null)
        {
            return Result.Failure(Error.NotFound("Accounting.JobNotFound", "Jobul nu există."));
        }

        string period = AccountingJson.Deserialize<MonthJobParameters>(job.ParametersJson, new MonthJobParameters(string.Empty)).Period;
        MonthJobResults results = AccountingJson.Deserialize(job.ResultJson, new MonthJobResults([], []));
        var settings = TaxEngineSettings.From(options.Value);

        List<ScopePfa> pfas = await AccountingScope.InPeriodAsync(db, period, cancellationToken);
        if (job.Type == BackgroundJobType.GenerateDeclarations)
        {
            List<Guid> ready = await db.PfaMonthChecks
                .Where(c => c.Period == period && c.Status == PfaMonthStatus.Ready)
                .Select(c => c.PfaRegistrationId)
                .ToListAsync(cancellationToken);
            List<Guid> declared = await db.Declarations.Where(d => d.Period == period).Select(d => d.PfaRegistrationId).Distinct().ToListAsync(cancellationToken);
            pfas = [.. pfas.Where(p => ready.Contains(p.Id) && !declared.Contains(p.Id))];
        }
        else if (job.Type != BackgroundJobType.ProcessPeriod)
        {
            job.Status = BackgroundJobStatus.Failed;
            results.Errors.Add(new JobResultItem(null, null, "Tipul de job nu e disponibil încă."));
            job.ResultJson = AccountingJson.Serialize(results);
            job.FinishedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        job.Status = BackgroundJobStatus.Running;
        job.ProgressTotal = pfas.Count;
        job.ProgressDone = 0;
        await db.SaveChangesAsync(cancellationToken);

        foreach (ScopePfa pfa in pfas)
        {
            (bool ok, string message) = job.Type == BackgroundJobType.ProcessPeriod
                ? await ProcessAsync(pfa, period, settings, cancellationToken)
                : await DeclarationGeneration.GenerateAsync(
                    db, pfa, await MonthData.LoadAsync(db, period, [pfa.Id], cancellationToken), settings, job.CreatedByUserId, cancellationToken);

            (ok ? results.Results : results.Errors).Add(new JobResultItem(pfa.Id, pfa.Name, message));
            job.ProgressDone++;
            job.ResultJson = AccountingJson.Serialize(results);
            await db.SaveChangesAsync(cancellationToken);
        }

        job.Status = BackgroundJobStatus.Completed;
        job.FinishedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<(bool Ok, string Message)> ProcessAsync(ScopePfa pfa, string period, TaxEngineSettings settings, CancellationToken cancellationToken)
    {
        List<Guid> queued = await db.PlatformDocuments
            .Where(d => d.PfaRegistrationId == pfa.Id && d.Period == period && d.Status == PlatformDocumentStatus.Extracting)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
        foreach (Guid id in queued)
        {
            await extraction.Handle(new RunPlatformDocumentExtractionCommand(id), cancellationToken);
        }

        MonthData data = await MonthData.LoadAsync(db, period, [pfa.Id], cancellationToken);
        PreCheckResult result = PreCheck.Evaluate(pfa, data, settings);
        await PreCheck.SaveAsync(db, pfa.Id, period, result, cancellationToken);

        string status = result.Status switch
        {
            PfaMonthStatus.Ready => "Gata",
            PfaMonthStatus.MissingDocuments => "Document lipsă",
            _ => "Necesită verificare",
        };
        return (true, result.Reasons.Count > 0 ? $"{status}: {result.Reasons[0]}" : status);
    }
}

/// <summary><c>GET /accounting/jobs/{jobId}</c></summary>
public sealed record GetJobQuery(Guid JobId) : IQuery<JobDto>;

internal sealed class GetJobQueryHandler(IApplicationDbContext db) : IQueryHandler<GetJobQuery, JobDto>
{
    public async Task<Result<JobDto>> Handle(GetJobQuery query, CancellationToken cancellationToken)
    {
        BackgroundJob? job = await db.BackgroundJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == query.JobId, cancellationToken);
        if (job is null)
        {
            return Result.Failure<JobDto>(Error.NotFound("Accounting.JobNotFound", "Jobul nu există."));
        }

        MonthJobResults results = AccountingJson.Deserialize(job.ResultJson, new MonthJobResults([], []));
        StoredFileRef? file = null;
        if (job.FileDocumentId is { } fileId &&
            await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == fileId, cancellationToken) is Document document)
        {
            file = new StoredFileRef(document.Id, document.OriginalFileName, document.ContentType, document.FileSize, string.Empty);
        }

        return new JobDto(
            job.Id,
            job.Type,
            job.Status,
            new JobProgress(job.ProgressDone, job.ProgressTotal),
            results.Results,
            results.Errors,
            job.CreatedAtUtc,
            job.FinishedAtUtc,
            file);
    }
}

/// <summary><c>POST /accounting/periods/{period}/confirm-clean-documents</c> (Decizii pct. 4).</summary>
public sealed record ConfirmCleanDocumentsCommand(string Period) : ICommand<ConfirmBulkResult>;

/// <summary>
/// Confirmă în bloc documentele lunii care așteaptă doar confirmarea (toate verificările trecute),
/// pe toate PFA-urile, apoi reface pre-check-ul celor atinse.
/// </summary>
internal sealed class ConfirmCleanDocumentsCommandHandler(IApplicationDbContext db, IUserContext userContext, IOptions<AccountingOptions> options)
    : ICommandHandler<ConfirmCleanDocumentsCommand, ConfirmBulkResult>
{
    public async Task<Result<ConfirmBulkResult>> Handle(ConfirmCleanDocumentsCommand command, CancellationToken cancellationToken)
    {
        if (!PlatformDocumentSupport.IsValidPeriod(command.Period))
        {
            return Result.Failure<ConfirmBulkResult>(AccountingErrors.InvalidPeriod);
        }

        List<Guid> scope = [.. (await AccountingScope.InPeriodAsync(db, command.Period, cancellationToken)).Select(p => p.Id)];
        List<PlatformDocument> pending = await db.PlatformDocuments
            .Where(d => d.Period == command.Period && scope.Contains(d.PfaRegistrationId) && d.Status == PlatformDocumentStatus.PendingConfirmation)
            .ToListAsync(cancellationToken);

        var confirmed = new List<Guid>();
        var skipped = new List<SkippedItem>();
        foreach (PlatformDocument document in pending)
        {
            Result<IReadOnlyList<DocumentCheck>> result = await PlatformDocumentConfirmation.ConfirmAsync(
                db, document, userContext.UserId, options.Value, "Confirmare în bloc a documentelor fără probleme", cancellationToken);
            if (result.IsSuccess)
            {
                confirmed.Add(document.Id);
            }
            else
            {
                skipped.Add(new SkippedItem(document.Id, result.Error.Description));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (Guid pfaId in pending.Select(d => d.PfaRegistrationId).Distinct())
        {
            await PreCheck.RefreshIfProcessedAsync(db, pfaId, command.Period, options.Value, cancellationToken);
        }

        return new ConfirmBulkResult(confirmed, skipped);
    }
}
