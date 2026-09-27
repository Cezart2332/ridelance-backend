using System.Globalization;
using System.IO.Compression;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.Pfas;
using Application.Accounting.Registers;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Handover;

internal sealed record HandoverParameters(Guid PfaId);

internal sealed record HandoverResults(List<JobResultItem> Results, List<JobResultItem> Errors);

/// <summary><c>POST /accounting/pfas/{pfaId}/handover-package</c> → <c>{ jobId }</c>.</summary>
public sealed record StartHandoverPackageCommand(Guid PfaId) : ICommand<JobRef>;

internal sealed class StartHandoverPackageCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<StartHandoverPackageCommand, JobRef>
{
    public async Task<Result<JobRef>> Handle(StartHandoverPackageCommand command, CancellationToken cancellationToken)
    {
        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == command.PfaId, cancellationToken))
        {
            return Result.Failure<JobRef>(AccountingErrors.PfaNotFound);
        }

        var job = new BackgroundJob
        {
            Id = Guid.NewGuid(),
            Type = BackgroundJobType.HandoverPackage,
            Status = BackgroundJobStatus.Queued,
            ParametersJson = AccountingJson.Serialize(new HandoverParameters(command.PfaId)),
            ResultJson = AccountingJson.Serialize(new HandoverResults([], [])),
            CreatedByUserId = userContext.UserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return new JobRef(job.Id);
    }
}

/// <summary>Rulează jobul dosarului de predare (apelat de <c>AccountingJobRunner</c>).</summary>
public sealed record RunHandoverPackageCommand(Guid JobId) : ICommand;

/// <summary>
/// Dosarul de predare (spec contabilitate B8): <c>RIDElance_PFA_{CUI}_{an}.zip</c> cu registrele
/// (RJIP, REF — intermediar dacă anul nu e închis —, ultimul Registru-inventar), ledger-ul în Excel,
/// documentele originale pe subfoldere, declarațiile (XML, PDF, recipise) și
/// <c>Sumar_predare.pdf</c>. Anul e cel al încetării colaborării, altfel anul curent.
/// </summary>
/// <remarks>
/// Structura exactă de foldere vine din documentul clientului (secțiunea 13), care nu e încă în
/// repo: cea de aici urmează lista din spec și se aliniază când documentul e disponibil.
/// </remarks>
internal sealed class RunHandoverPackageCommandHandler(
    IApplicationDbContext db,
    DeclarationFiles files,
    IRegisterExporter exporter,
    IQueryHandler<GetRefQuery, RefView> refQuery,
    IQueryHandler<ExportRjipQuery, RegisterFile> rjipExport,
    IQueryHandler<ExportRefQuery, RegisterFile> refExport,
    IQueryHandler<ExportInventoryQuery, RegisterFile> inventoryExport)
    : ICommandHandler<RunHandoverPackageCommand>
{
    private const int Steps = 7;

    /// <summary>Căile deja scrise în arhivă: în modul de creare, ZipArchive nu le poate căuta.</summary>
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

    public async Task<Result> Handle(RunHandoverPackageCommand command, CancellationToken cancellationToken)
    {
        BackgroundJob? job = await db.BackgroundJobs.SingleOrDefaultAsync(j => j.Id == command.JobId, cancellationToken);
        if (job is null)
        {
            return Result.Failure(Error.NotFound("Accounting.JobNotFound", "Jobul nu există."));
        }

        Guid pfaId = AccountingJson.Deserialize(job.ParametersJson, new HandoverParameters(Guid.Empty)).PfaId;
        var results = new HandoverResults([], []);
        PfaAccountingSummary? summary = await PfaSummaries.BuildAsync(db, pfaId, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        if (summary is null)
        {
            return await FailAsync(job, results, null, "PFA-ul nu există.", cancellationToken);
        }

        job.Status = BackgroundJobStatus.Running;
        job.ProgressTotal = Steps;
        job.ProgressDone = 0;
        await db.SaveChangesAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly last = summary.Engagement.EndDate is { } ended && summary.Engagement.Status == EngagementStatus.Inactive ? ended : today;
        int year = last.Year;
        var yearStart = new DateOnly(year, 1, 1);
        DateOnly from = summary.Engagement.StartDate > yearStart ? summary.Engagement.StartDate : yearStart;
        DateOnly to = last < new DateOnly(year, 12, 31) ? last : new DateOnly(year, 12, 31);
        string cui = string.IsNullOrWhiteSpace(summary.Cui) ? summary.Id.ToString("N")[..8] : summary.Cui;
        var counts = new Dictionary<string, int>();

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            async Task Step(string message, Func<Task<int>> run)
            {
                int added = await run();
                counts[message] = added;
                results.Results.Add(new JobResultItem(pfaId, summary.Name, $"Adăugat în arhivă: {message} ({added} {(added == 1 ? "fișier" : "fișiere")})."));
                job.ProgressDone++;
                job.ResultJson = AccountingJson.Serialize(results);
                await db.SaveChangesAsync(cancellationToken);
            }

            RefView refView = (await refQuery.Handle(new GetRefQuery(pfaId, year), cancellationToken)).Value;
            // Anul neînchis: REF-ul din dosar e situația intermediară la data predării (sau a încetării).
            DateOnly? refAsOf = refView.Status == RefStatus.Final ? null : to;

            await Step("RJIP", () => AddRegisterAsync(zip, "01_Registre", rjipExport, new ExportRjipQuery(pfaId, from, to, RegisterFormat.Pdf), new ExportRjipQuery(pfaId, from, to, RegisterFormat.Xlsx), cancellationToken));
            await Step(refAsOf is null ? "REF final" : "REF intermediar", () => AddRegisterAsync(zip, "01_Registre", refExport, new ExportRefQuery(pfaId, year, RegisterFormat.Pdf, refAsOf), new ExportRefQuery(pfaId, year, RegisterFormat.Xlsx, refAsOf), cancellationToken));
            await Step("Registru-inventar", () => AddRegisterAsync(zip, "01_Registre", inventoryExport, new ExportInventoryQuery(pfaId, year, RegisterFormat.Pdf), new ExportInventoryQuery(pfaId, year, RegisterFormat.Xlsx), cancellationToken));
            await Step("Ledger (Excel)", () => AddLedgerAsync(zip, pfaId, summary, from, to, cui, cancellationToken));
            await Step("Documente originale", () => AddDocumentsAsync(zip, pfaId, year, cancellationToken));
            await Step("Declarații (XML + PDF) și recipise", () => AddDeclarationsAsync(zip, pfaId, year, cancellationToken));
            DateOnly retention = summary.RetentionUntil ?? await RetentionService.MinimumRetentionUntilAsync(db, year, cancellationToken);
            await Step("Sumar_predare.pdf", () => AddSummaryAsync(zip, summary, year, from, to, refView, refAsOf, retention, counts));
        }

        string fileName = $"RIDElance_PFA_{cui}_{year}.zip";
        Domain.Documents.Document document = await files.StoreAsync(pfaId, buffer.ToArray(), fileName, "application/zip", cancellationToken);
        job.FileDocumentId = document.Id;
        job.Status = BackgroundJobStatus.Completed;
        job.FinishedAtUtc = DateTime.UtcNow;
        job.ResultJson = AccountingJson.Serialize(results);
        AccountingAudit.Record(db, pfaId, "HandoverPackage", job.Id, "GENERATE", null, new { fileName, year }, null, job.CreatedByUserId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result> FailAsync(BackgroundJob job, HandoverResults results, Guid? pfaId, string message, CancellationToken cancellationToken)
    {
        results.Errors.Add(new JobResultItem(pfaId, null, message));
        job.Status = BackgroundJobStatus.Failed;
        job.ResultJson = AccountingJson.Serialize(results);
        job.FinishedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<int> AddRegisterAsync<TQuery>(
        ZipArchive zip,
        string folder,
        IQueryHandler<TQuery, RegisterFile> export,
        TQuery pdf,
        TQuery xlsx,
        CancellationToken cancellationToken)
        where TQuery : IQuery<RegisterFile>
    {
        int added = 0;
        foreach (TQuery query in new[] { pdf, xlsx })
        {
            Result<RegisterFile> file = await export.Handle(query, cancellationToken);
            if (file.IsSuccess)
            {
                await AddAsync(zip, $"{folder}/{file.Value.FileName}", file.Value.Content, cancellationToken);
                added++;
            }
        }

        return added;
    }

    private async Task<int> AddLedgerAsync(ZipArchive zip, Guid pfaId, PfaAccountingSummary summary, DateOnly from, DateOnly to, string cui, CancellationToken cancellationToken)
    {
        List<LedgerEntry> entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfaId && e.Date >= from && e.Date <= to)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var document = new RegisterDocument(
            "LEDGER — ÎNREGISTRĂRI CONTABILE",
            "Evidență internă RIDElance",
            [$"{summary.Name} — CUI {summary.Cui}", $"Perioada {RegisterData.Date(from)} – {RegisterData.Date(to)}"],
            [
                new("Data"), new("Document", Width: 1.4f), new("Sursă"), new("Contrapartidă", Width: 1.4f), new("Descriere", Width: 2.2f),
                new("Tip"), new("Metodă"), new("Sumă", Numeric: true), new("Monedă", Width: 0.6f), new("Categorie"),
                new("Deductibil", Numeric: true), new("Status"), new("Perioadă închisă", Width: 0.8f),
            ],
            null,
            [.. entries.Select(e => new RegisterLine(
            [
                RegisterData.Date(e.Date), e.DocumentLabel, Code(e.Source), e.Counterparty, e.Description, Code(e.TransactionType), Code(e.PaymentMethod),
                e.Amount, e.Currency, e.Category, e.DeductibleAmount, Code(e.Status), e.ClosedPeriodFlag ? "Da" : null,
            ]))],
            []);
        await AddAsync(zip, $"02_Ledger/Ledger_{cui}_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx", exporter.ToXlsx(document), cancellationToken);
        return 1;
    }

    private async Task<int> AddDocumentsAsync(ZipArchive zip, Guid pfaId, int year, CancellationToken cancellationToken)
    {
        string prefix = $"{year}-";
        var platform = await db.PlatformDocuments.AsNoTracking()
            .Where(d => d.PfaRegistrationId == pfaId && d.Period.StartsWith(prefix))
            .Select(d => new { d.SourceDocumentId, d.Period, d.Platform })
            .ToListAsync(cancellationToken);
        List<Guid> expenses = await db.ExpenseDocuments.AsNoTracking()
            .Where(d => d.PfaRegistrationId == pfaId && (d.Date == null ? d.UploadedAtUtc.Year == year : d.Date.Value.Year == year))
            .Select(d => d.DocumentId)
            .ToListAsync(cancellationToken);
        var zReports = await db.ZReports.AsNoTracking()
            .Where(z => z.PfaRegistrationId == pfaId && z.Date.Year == year && z.DocumentId != null)
            .Select(z => new { z.DocumentId, z.ZNumber })
            .ToListAsync(cancellationToken);
        Guid? evidence = await db.CashRegisterStates.AsNoTracking()
            .Where(c => c.PfaRegistrationId == pfaId)
            .Select(c => c.EvidenceDocumentId)
            .FirstOrDefaultAsync(cancellationToken);

        int added = 0;
        foreach (var document in platform)
        {
            added += await AddStoredAsync(zip, $"03_Documente/Platforme/{document.Period}/{document.Platform}", document.SourceDocumentId, cancellationToken);
        }

        foreach (Guid document in expenses)
        {
            added += await AddStoredAsync(zip, "03_Documente/Cheltuieli", document, cancellationToken);
        }

        foreach (var report in zReports)
        {
            added += await AddStoredAsync(zip, "03_Documente/Rapoarte_Z", report.DocumentId, cancellationToken);
        }

        added += await AddStoredAsync(zip, "03_Documente/Casa_de_marcat", evidence, cancellationToken);
        return added;
    }

    private async Task<int> AddDeclarationsAsync(ZipArchive zip, Guid pfaId, int year, CancellationToken cancellationToken)
    {
        string prefix = $"{year}-";
        var versions = await db.DeclarationVersions.AsNoTracking()
            .Where(v => v.Declaration.PfaRegistrationId == pfaId && v.Declaration.Period.StartsWith(prefix))
            .Select(v => new { v.Declaration.Period, v.Declaration.Type, v.VersionNo, v.XmlDocumentId, v.PdfDocumentId, v.ReceiptDocumentId })
            .ToListAsync(cancellationToken);

        int added = 0;
        foreach (var version in versions.OrderBy(v => v.Period, StringComparer.Ordinal).ThenBy(v => v.Type).ThenBy(v => v.VersionNo))
        {
            string folder = $"04_Declaratii/{version.Period}/{version.Type}_v{version.VersionNo}";
            added += await AddStoredAsync(zip, folder, version.XmlDocumentId, cancellationToken);
            added += await AddStoredAsync(zip, folder, version.PdfDocumentId, cancellationToken);
            added += await AddStoredAsync(zip, $"{folder}/Recipisa", version.ReceiptDocumentId, cancellationToken);
        }

        return added;
    }

    private async Task<int> AddSummaryAsync(
        ZipArchive zip,
        PfaAccountingSummary summary,
        int year,
        DateOnly from,
        DateOnly to,
        RefView refView,
        DateOnly? refAsOf,
        DateOnly retention,
        Dictionary<string, int> counts)
    {
        var lines = new List<RegisterLine>
        {
            new(["PFA", summary.Name]),
            new(["CUI", summary.Cui]),
            new(["Anul dosarului", year.ToString(CultureInfo.InvariantCulture)]),
            new(["Perioada acoperită", $"{RegisterData.Date(from)} – {RegisterData.Date(to)}"]),
            new(["Colaborare contabilă", Engagement(summary.Engagement)]),
            new(["Păstrare obligatorie până la", RegisterData.Date(retention)]),
            new(["REF", refAsOf is { } asOf ? $"situație intermediară la {RegisterData.Date(asOf)}" : "final"]),
        };
        lines.AddRange(refView.Rows.Select(row => new RegisterLine([$"REF — {row.CalculationElement}", RegisterData.Amount(row.Value) + " lei"])));
        lines.AddRange(counts.Select(pair => new RegisterLine([$"Conținut — {pair.Key}", $"{pair.Value} {(pair.Value == 1 ? "fișier" : "fișiere")}"])));

        var document = new RegisterDocument(
            "SUMAR PREDARE",
            "Dosar de predare RIDElance",
            [$"{summary.Name} — CUI {summary.Cui}", $"Generat la {RegisterData.Date(DateOnly.FromDateTime(DateTime.UtcNow))}"],
            [new("Element", Width: 1.5f), new("Valoare", Width: 3f)],
            null,
            lines,
            [
                "Structura: 01_Registre (RJIP, REF, Registru-inventar, PDF și Excel), 02_Ledger (Excel), 03_Documente (originale, pe tipuri), " +
                "04_Declaratii (XML, PDF DUKIntegrator și recipise, pe luni și versiuni).",
                "Documentele nu se șterg automat; termenul de păstrare de mai sus e cel minim.",
            ]);
        await AddAsync(zip, "Sumar_predare.pdf", exporter.ToPdf(document), CancellationToken.None);
        return 1;
    }

    private static string Engagement(EngagementInfo engagement)
    {
        if (engagement.EndDate is not { } end)
        {
            return $"din {RegisterData.Date(engagement.StartDate)} (activă)";
        }

        string state = engagement.Status == EngagementStatus.Inactive ? "încheiată" : "activă";
        return $"{RegisterData.Date(engagement.StartDate)} – {RegisterData.Date(end)} ({state})";
    }

    private async Task<int> AddStoredAsync(ZipArchive zip, string folder, Guid? documentId, CancellationToken cancellationToken)
    {
        if (await files.ReadAsync(documentId, cancellationToken) is not { } stored)
        {
            return 0;
        }

        string name = string.Concat(stored.Document.OriginalFileName.Split(Path.GetInvalidFileNameChars()));
        string path = $"{folder}/{name}";
        if (_paths.Contains(path))
        {
            path = $"{folder}/{stored.Document.Id.ToString("N")[..8]}_{name}";
        }

        await AddAsync(zip, path, stored.Content, cancellationToken);
        return 1;
    }

    private async Task AddAsync(ZipArchive zip, string path, byte[] content, CancellationToken cancellationToken)
    {
        _paths.Add(path);
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using Stream stream = await entry.OpenAsync(cancellationToken);
        await stream.WriteAsync(content, cancellationToken);
    }

    private static string Code<TEnum>(TEnum value)
        where TEnum : struct, Enum => AccountingJson.Serialize(value).Trim('"');
}

/// <summary><c>GET /accounting/jobs/{jobId}/file</c> — fișierul produs de job (arhiva dosarului de predare).</summary>
public sealed record GetJobFileQuery(Guid JobId) : IQuery<RegisterFile>;

internal sealed class GetJobFileQueryHandler(IApplicationDbContext db, DeclarationFiles files) : IQueryHandler<GetJobFileQuery, RegisterFile>
{
    public async Task<Result<RegisterFile>> Handle(GetJobFileQuery query, CancellationToken cancellationToken)
    {
        Guid? documentId = await db.BackgroundJobs.AsNoTracking()
            .Where(j => j.Id == query.JobId)
            .Select(j => j.FileDocumentId)
            .FirstOrDefaultAsync(cancellationToken);
        if (await files.ReadAsync(documentId, cancellationToken) is not { } stored)
        {
            return Result.Failure<RegisterFile>(Error.NotFound("Accounting.JobFileNotFound", "Jobul nu are (încă) un fișier."));
        }

        return new RegisterFile(stored.Content, stored.Document.OriginalFileName, stored.Document.ContentType);
    }
}
