using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Data;
using Application.Accounting.Declarations;
using Domain.Accounting;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Spv;

public static class SpvErrors
{
    public static readonly Error InvalidKey = Error.Problem("Spv.InvalidKey", "Cheia aplicației SPV nu e validă sau a fost revocată.");

    public static readonly Error RunInProgress = Error.Conflict("Spv.RunInProgress", "O altă trimitere e în curs. Încearcă din nou peste câteva minute.");

    public static readonly Error RunNotFound = Error.NotFound("Spv.RunNotFound", "Trimiterea nu există sau a expirat. Pornește una nouă.");

    public static readonly Error UnknownType = Error.Problem("Spv.UnknownType", "Tipul de cerere SPV nu e cunoscut.");

    public static readonly Error InvalidCui = Error.Problem("Spv.InvalidCui", "PFA-ul nu are un CUI valid.");

    public static readonly Error MessageNotFound = Error.NotFound("Spv.MessageNotFound", "Mesajul SPV nu există.");

    public static readonly Error NameRequired = Error.Problem("Spv.NameRequired", "Dă un nume cheii (de ex. calculatorul pe care o folosești).");
}

/// <summary>Cererile SPV pe care le trimite aplicația și parametrii pe care îi acceptă fiecare.</summary>
public static class SpvRequestTypes
{
    public static readonly IReadOnlyDictionary<string, string[]> Parameters = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["VECTOR FISCAL"] = [],
        ["Obligatii de plata"] = [],
        ["Nota obligatiilor de plata"] = [],
        ["Situatie Sintetica"] = ["an", "luna"],
        ["Fisa Rol"] = [],
        ["Istoric declaratii"] = ["an"],
        ["Duplicat Recipisa"] = ["numar_inregistrare"],
        ["D100"] = ["an", "luna"],
        ["D301"] = ["an", "luna"],
        ["D390"] = ["an", "luna"],
        ["D212"] = ["an"],
    };
}

public sealed record SpvRequestToSend(Guid Id, string Type, string Cui, IReadOnlyDictionary<string, string> Parameters);

/// <summary>Ce primește aplicația la pornirea unei trimiteri.</summary>
/// <param name="Days">Câte zile să ceară la <c>listaMesaje</c>: de la ultima trimitere reușită, cu două zile în plus, maximum 60.</param>
public sealed record SpvRunStart(Guid RunId, int Days, IReadOnlyList<string> Cuis, IReadOnlyList<SpvRequestToSend> Requests);

/// <summary>Un mesaj trimis de aplicație, cum l-a citit din <c>listaMesaje</c>.</summary>
public sealed record SpvIncomingMessage(string Id, string Cif, string Type, DateTime CreatedAtUtc, string? RequestId, string? Details);

public sealed record SpvIncomingFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// SPV prin aplicația desktop RIDElance SPV. Aplicația folosește stickul împuternicitului și doar
/// transportă: listează, descarcă, trimite cererile. Serverul ține minte ultima trimitere reușită,
/// ignoră mesajele deja primite (id ANAF + hash), asociază fiecare mesaj clientului după CIF și
/// leagă recipisele de declarații.
/// </summary>
internal sealed class SpvService(IApplicationDbContext db, DeclarationFiles files, DeclarationActions declarations)
{
    public const string KeyPrefix = "rdl_spv_";

    /// <summary>ANAF dă mesajele din ultimele 60 de zile.</summary>
    public const int MaxDays = 60;

    private const int OverlapDays = 2;

    /// <summary>O trimitere fără niciun semn de la aplicație atâta timp e considerată abandonată.</summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    // ---- chei ----

    public async Task<Result<(SpvAgentKey Key, string Secret)>> CreateKeyAsync(Guid userId, string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<(SpvAgentKey, string)>(SpvErrors.NameRequired);
        }

        string secret = KeyPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToUpperInvariant();
        var key = new SpvAgentKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name.Trim()[..Math.Min(name.Trim().Length, 100)],
            Prefix = secret[..(KeyPrefix.Length + 6)],
            KeyHash = Hash(secret),
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.SpvAgentKeys.Add(key);
        AccountingAudit.Record(db, null, nameof(SpvAgentKey), key.Id, "CREATE", null, new { key.Name, key.Prefix }, null, userId);
        await db.SaveChangesAsync(cancellationToken);
        return (key, secret);
    }

    public async Task RevokeKeyAsync(Guid keyId, Guid userId, CancellationToken cancellationToken)
    {
        SpvAgentKey? key = await db.SpvAgentKeys.SingleOrDefaultAsync(k => k.Id == keyId, cancellationToken);
        if (key is null || key.RevokedAtUtc is not null)
        {
            return;
        }

        key.RevokedAtUtc = DateTime.UtcNow;
        AccountingAudit.Record(db, null, nameof(SpvAgentKey), key.Id, "REVOKE", null, null, null, userId);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Cheia din antetul aplicației: validă, nerevocată, a unui admin.</summary>
    public async Task<Result<SpvAgentKey>> AuthenticateAsync(string? secret, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secret) || !secret.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return Result.Failure<SpvAgentKey>(SpvErrors.InvalidKey);
        }

        string hash = Hash(secret.Trim());
        SpvAgentKey? key = await db.SpvAgentKeys.SingleOrDefaultAsync(k => k.KeyHash == hash && k.RevokedAtUtc == null, cancellationToken);
        if (key is null || !await db.Users.AnyAsync(u => u.Id == key.UserId && u.Role == UserRole.Admin, cancellationToken))
        {
            return Result.Failure<SpvAgentKey>(SpvErrors.InvalidKey);
        }

        key.LastUsedAtUtc = DateTime.UtcNow;
        return key;
    }

    // ---- trimiteri ----

    public async Task<Result<SpvRunStart>> StartRunAsync(SpvAgentKey key, string? machine, string? agentVersion, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        await ExpireStaleRunsAsync(now, cancellationToken);
        // Salvat înainte de verificare: o trimitere abandonată nu mai blochează.
        await db.SaveChangesAsync(cancellationToken);
        if (await db.SpvSyncRuns.AnyAsync(r => r.Status == SpvSyncRunStatus.Running, cancellationToken))
        {
            return Result.Failure<SpvRunStart>(SpvErrors.RunInProgress);
        }

        DateTime? lastSuccess = await db.SpvSyncRuns
            .Where(r => r.Status == SpvSyncRunStatus.Completed)
            .OrderByDescending(r => r.StartedAtUtc)
            .Select(r => (DateTime?)r.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        int days = DaysSince(lastSuccess, now);

        var run = new SpvSyncRun
        {
            Id = Guid.NewGuid(),
            AgentKeyId = key.Id,
            Status = SpvSyncRunStatus.Running,
            Machine = Limit(machine, 100),
            AgentVersion = Limit(agentVersion, 40),
            Days = days,
            StartedAtUtc = now,
            LeaseUntilUtc = now + Lease,
        };
        db.SpvSyncRuns.Add(run);

        List<SpvRequest> queued = await db.SpvRequests.Where(r => r.Status == SpvRequestStatus.Queued).OrderBy(r => r.CreatedAtUtc).ToListAsync(cancellationToken);
        foreach (SpvRequest request in queued)
        {
            request.Status = SpvRequestStatus.Sending;
            request.ClaimedAtUtc = now;
            request.ClaimedByRunId = run.Id;
        }

        List<string> cuis = await db.PfaRegistrations
            .Where(p => p.Cui != null && p.User.DeletedAtUtc == null)
            .Select(p => p.Cui!)
            .ToListAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new SpvRunStart(
            run.Id,
            days,
            [.. cuis.Select(Digits).Where(cui => cui.Length > 0).Distinct().Order(StringComparer.Ordinal)],
            [.. queued.Select(r => new SpvRequestToSend(r.Id, r.Type, r.Cui, AccountingJson.Deserialize<Dictionary<string, string>>(r.ParametersJson, []) ?? []))]);
    }

    /// <summary>Din id-urile listate la ANAF, cele pe care serverul nu le are: doar pe ele le descarcă aplicația.</summary>
    public async Task<Result<IReadOnlyList<string>>> NewIdsAsync(SpvAgentKey key, Guid runId, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        Result<SpvSyncRun> run = await RunAsync(key, runId, cancellationToken);
        if (run.IsFailure)
        {
            return Result.Failure<IReadOnlyList<string>>(run.Error);
        }

        run.Value.Listed = ids.Count;
        List<string> distinct = [.. ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)];
        HashSet<string> known = [.. await db.SpvMessages.Where(m => distinct.Contains(m.AnafMessageId)).Select(m => m.AnafMessageId).ToListAsync(cancellationToken)];
        await db.SaveChangesAsync(cancellationToken);
        return distinct.Where(id => !known.Contains(id)).ToList();
    }

    /// <summary>
    /// Un mesaj cu documentul lui. Idempotent: un id deja primit nu schimbă nimic, deci o trimitere
    /// întreruptă se poate relua oricând.
    /// </summary>
    public async Task<Result<SpvMessage>> ReceiveAsync(SpvAgentKey key, Guid runId, SpvIncomingMessage incoming, SpvIncomingFile? file, CancellationToken cancellationToken)
    {
        Result<SpvSyncRun> run = await RunAsync(key, runId, cancellationToken);
        if (run.IsFailure)
        {
            return Result.Failure<SpvMessage>(run.Error);
        }

        if (await db.SpvMessages.SingleOrDefaultAsync(m => m.AnafMessageId == incoming.Id, cancellationToken) is { } existing)
        {
            await db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        string cif = Digits(incoming.Cif);
        Guid? pfaId = cif.Length == 0
            ? null
            : await db.PfaRegistrations.Where(p => p.Cui != null && (p.Cui == cif || p.Cui == "RO" + cif)).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);

        var message = new SpvMessage
        {
            Id = Guid.NewGuid(),
            AnafMessageId = Limit(incoming.Id, 64),
            Cif = Limit(cif.Length > 0 ? cif : incoming.Cif, 32),
            PfaRegistrationId = pfaId,
            Type = Limit(incoming.Type, 100),
            AnafCreatedAtUtc = DateTime.SpecifyKind(incoming.CreatedAtUtc, DateTimeKind.Utc),
            RequestId = incoming.RequestId is null ? null : Limit(incoming.RequestId, 64),
            Details = incoming.Details is null ? null : Limit(incoming.Details, 2000),
            ReceivedAtUtc = DateTime.UtcNow,
            Status = SpvMessageStatus.New,
        };
        db.SpvMessages.Add(message);

        if (file is { Content.Length: > 0 } && pfaId is { } owner)
        {
            message.FileHash = Hash(file.Content);
            Guid? same = await db.SpvMessages
                .Where(m => m.PfaRegistrationId == owner && m.FileHash == message.FileHash && m.DocumentId != null)
                .Select(m => m.DocumentId)
                .FirstOrDefaultAsync(cancellationToken);
            message.DocumentId = same ?? (await files.StoreAsync(owner, file.Content, Limit(file.FileName, 200), file.ContentType, cancellationToken, DocumentOrigin.SystemGenerated)).Id;
        }

        run.Value.Received++;
        await db.SaveChangesAsync(cancellationToken);

        await ProcessAsync(message, file, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task<Result> RequestResultAsync(SpvAgentKey key, Guid runId, Guid requestId, string? anafRequestId, string? error, CancellationToken cancellationToken)
    {
        Result<SpvSyncRun> run = await RunAsync(key, runId, cancellationToken);
        if (run.IsFailure)
        {
            return run;
        }

        SpvRequest? request = await db.SpvRequests.SingleOrDefaultAsync(r => r.Id == requestId && r.ClaimedByRunId == runId, cancellationToken);
        if (request is null)
        {
            return Result.Success();
        }

        if (!string.IsNullOrWhiteSpace(anafRequestId))
        {
            request.Status = SpvRequestStatus.Sent;
            request.AnafRequestId = Limit(anafRequestId, 64);
            request.SentAtUtc = DateTime.UtcNow;
            request.Error = null;
            run.Value.RequestsSent++;
        }
        else
        {
            request.Status = SpvRequestStatus.Failed;
            request.Error = Limit(error ?? "ANAF a refuzat cererea.", 1000);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> FinishRunAsync(SpvAgentKey key, Guid runId, string? error, CancellationToken cancellationToken)
    {
        Result<SpvSyncRun> run = await RunAsync(key, runId, cancellationToken);
        if (run.IsFailure)
        {
            return run;
        }

        run.Value.Status = string.IsNullOrWhiteSpace(error) ? SpvSyncRunStatus.Completed : SpvSyncRunStatus.Failed;
        run.Value.Error = string.IsNullOrWhiteSpace(error) ? null : Limit(error, 1000);
        run.Value.FinishedAtUtc = DateTime.UtcNow;
        await ReleaseRequestsAsync(runId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ---- cereri din web ----

    public async Task<Result<SpvRequest>> QueueRequestAsync(Guid pfaId, string type, IReadOnlyDictionary<string, string>? parameters, Guid userId, CancellationToken cancellationToken)
    {
        if (!SpvRequestTypes.Parameters.TryGetValue(type, out string[]? allowed))
        {
            return Result.Failure<SpvRequest>(SpvErrors.UnknownType);
        }

        string? raw = await db.PfaRegistrations.Where(p => p.Id == pfaId).Select(p => p.Cui).SingleOrDefaultAsync(cancellationToken);
        string cui = Digits(raw);
        if (cui.Length == 0 || !CuiValidator.Validate(cui).IsValid)
        {
            return Result.Failure<SpvRequest>(SpvErrors.InvalidCui);
        }

        var values = (parameters ?? new Dictionary<string, string>())
            .Where(pair => allowed.Contains(pair.Key, StringComparer.Ordinal) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.Ordinal);
        var request = new SpvRequest
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfaId,
            Cui = cui,
            Type = type,
            ParametersJson = AccountingJson.Serialize(values),
            Status = SpvRequestStatus.Queued,
            RequestedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.SpvRequests.Add(request);
        AccountingAudit.Record(db, pfaId, nameof(SpvRequest), request.Id, "QUEUE", null, new { type, values }, null, userId);
        await db.SaveChangesAsync(cancellationToken);
        return request;
    }

    internal static int DaysSince(DateTime? lastSuccess, DateTime now) =>
        lastSuccess is { } last
            ? Math.Clamp((int)Math.Ceiling((now - last).TotalDays) + OverlapDays, 1, MaxDays)
            : MaxDays;

    // ---- procesarea unui mesaj ----

    private async Task ProcessAsync(SpvMessage message, SpvIncomingFile? file, CancellationToken cancellationToken)
    {
        if (message.PfaRegistrationId is not { } pfaId)
        {
            message.Status = SpvMessageStatus.NeedsAttention;
            message.Note = $"Niciun client cu CIF {message.Cif}.";
            return;
        }

        if (message.RequestId is { Length: > 0 } requestId &&
            await db.SpvRequests.SingleOrDefaultAsync(r => r.AnafRequestId == requestId && r.PfaRegistrationId == pfaId, cancellationToken) is { } request)
        {
            request.Status = SpvRequestStatus.Answered;
            request.AnswerMessageId = message.Id;
            message.SpvRequestId = request.Id;
            message.Status = SpvMessageStatus.Processed;
            message.Note = $"Răspuns la cererea „{request.Type}”.";
            return;
        }

        if (!SpvRecipisa.IsRecipisa(message.Type))
        {
            return;
        }

        SpvRecipisaInfo? info = SpvRecipisa.Read(message.Details);
        if (info is null)
        {
            message.Status = SpvMessageStatus.NeedsAttention;
            message.Note = "Recipisă fără tipul declarației în detalii.";
            return;
        }

        if (!Enum.TryParse(info.DeclarationType, ignoreCase: true, out DeclarationType type) || info.Period is null)
        {
            // D700, D212 și alte declarații pe care nu le generăm lunar: rămân în lista clientului.
            message.Note = info.Period is null ? $"Recipisă {info.DeclarationType}." : $"Recipisă {info.DeclarationType} {info.Period}.";
            return;
        }

        DeclarationVersion? version = await db.DeclarationVersions
            .Include(v => v.Declaration)
            .Where(v => v.Declaration.PfaRegistrationId == pfaId && v.Declaration.Type == type && v.Declaration.Period == info.Period)
            .OrderByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(cancellationToken);
        message.DeclarationVersionId = version?.Id;
        if (version is null)
        {
            message.Status = SpvMessageStatus.NeedsAttention;
            message.Note = $"Recipisă {info.DeclarationType} {info.Period}, fără declarație în RIDElance.";
            return;
        }

        if (version.Status == DeclarationStatus.Accepted)
        {
            message.Status = SpvMessageStatus.Processed;
            message.Note = $"Recipisă {info.DeclarationType} {info.Period}, deja atașată.";
            return;
        }

        if (version.Status != DeclarationStatus.Submitted || file is not { Content.Length: > 0 })
        {
            message.Status = SpvMessageStatus.NeedsAttention;
            message.Note = $"Recipisă {info.DeclarationType} {info.Period}: declarația nu e marcată depusă.";
            return;
        }

        Result attached = await declarations.UploadReceiptAsync(
            version.Id,
            new ReceiptFile(file.FileName, file.ContentType, file.Content),
            info.RegistrationNumber,
            null,
            cancellationToken);
        message.Status = attached.IsSuccess ? SpvMessageStatus.Processed : SpvMessageStatus.NeedsAttention;
        message.Note = attached.IsSuccess
            ? $"Recipisă {info.DeclarationType} {info.Period} atașată declarației."
            : $"Recipisă {info.DeclarationType} {info.Period}: {attached.Error.Description}";
    }

    private async Task<Result<SpvSyncRun>> RunAsync(SpvAgentKey key, Guid runId, CancellationToken cancellationToken)
    {
        SpvSyncRun? run = await db.SpvSyncRuns.SingleOrDefaultAsync(
            r => r.Id == runId && r.AgentKeyId == key.Id && r.Status == SpvSyncRunStatus.Running,
            cancellationToken);
        if (run is null)
        {
            return Result.Failure<SpvSyncRun>(SpvErrors.RunNotFound);
        }

        run.LeaseUntilUtc = DateTime.UtcNow + Lease;
        return run;
    }

    /// <summary>Trimiterile rămase „în lucru” (PC închis în timpul lor) se închid, iar cererile lor revin în coadă.</summary>
    private async Task ExpireStaleRunsAsync(DateTime now, CancellationToken cancellationToken)
    {
        List<SpvSyncRun> stale = await db.SpvSyncRuns.Where(r => r.Status == SpvSyncRunStatus.Running && r.LeaseUntilUtc < now).ToListAsync(cancellationToken);
        foreach (SpvSyncRun run in stale)
        {
            run.Status = SpvSyncRunStatus.Abandoned;
            run.FinishedAtUtc = now;
            await ReleaseRequestsAsync(run.Id, cancellationToken);
        }
    }

    private async Task ReleaseRequestsAsync(Guid runId, CancellationToken cancellationToken)
    {
        foreach (SpvRequest request in await db.SpvRequests.Where(r => r.ClaimedByRunId == runId && r.Status == SpvRequestStatus.Sending).ToListAsync(cancellationToken))
        {
            request.Status = SpvRequestStatus.Queued;
            request.ClaimedByRunId = null;
            request.ClaimedAtUtc = null;
        }
    }

    private static string Digits(string? value) => new([.. (value ?? string.Empty).Where(char.IsDigit)]);

    private static string Limit(string? value, int length)
    {
        string text = (value ?? string.Empty).Trim();
        return text.Length <= length ? text : text[..length];
    }

    private static string Hash(string value) => Hash(Encoding.UTF8.GetBytes(value));

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
}
