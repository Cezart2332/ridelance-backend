using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Declarations;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Spv;

// ---- aplicația desktop (autentificată cu cheia ei) ----

public sealed record StartSpvRunCommand(string? Key, string? Machine, string? AgentVersion) : ICommand<SpvRunStart>;

internal sealed class StartSpvRunCommandHandler(SpvService spv) : ICommandHandler<StartSpvRunCommand, SpvRunStart>
{
    public async Task<Result<SpvRunStart>> Handle(StartSpvRunCommand command, CancellationToken cancellationToken)
    {
        Result<SpvAgentKey> key = await spv.AuthenticateAsync(command.Key, cancellationToken);
        return key.IsFailure ? Result.Failure<SpvRunStart>(key.Error) : await spv.StartRunAsync(key.Value, command.Machine, command.AgentVersion, cancellationToken);
    }
}

public sealed record FilterNewSpvMessagesCommand(string? Key, Guid RunId, IReadOnlyList<string> Ids) : ICommand<IReadOnlyList<string>>;

internal sealed class FilterNewSpvMessagesCommandHandler(SpvService spv) : ICommandHandler<FilterNewSpvMessagesCommand, IReadOnlyList<string>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(FilterNewSpvMessagesCommand command, CancellationToken cancellationToken)
    {
        Result<SpvAgentKey> key = await spv.AuthenticateAsync(command.Key, cancellationToken);
        return key.IsFailure ? Result.Failure<IReadOnlyList<string>>(key.Error) : await spv.NewIdsAsync(key.Value, command.RunId, command.Ids, cancellationToken);
    }
}

public sealed record ReceiveSpvMessageCommand(string? Key, Guid RunId, SpvIncomingMessage Message, SpvIncomingFile? File) : ICommand;

internal sealed class ReceiveSpvMessageCommandHandler(SpvService spv) : ICommandHandler<ReceiveSpvMessageCommand>
{
    public async Task<Result> Handle(ReceiveSpvMessageCommand command, CancellationToken cancellationToken)
    {
        Result<SpvAgentKey> key = await spv.AuthenticateAsync(command.Key, cancellationToken);
        if (key.IsFailure)
        {
            return key;
        }

        Result<SpvMessage> received = await spv.ReceiveAsync(key.Value, command.RunId, command.Message, command.File, cancellationToken);
        return received.IsFailure ? Result.Failure(received.Error) : Result.Success();
    }
}

public sealed record ReportSpvRequestCommand(string? Key, Guid RunId, Guid RequestId, string? AnafRequestId, string? Error) : ICommand;

internal sealed class ReportSpvRequestCommandHandler(SpvService spv) : ICommandHandler<ReportSpvRequestCommand>
{
    public async Task<Result> Handle(ReportSpvRequestCommand command, CancellationToken cancellationToken)
    {
        Result<SpvAgentKey> key = await spv.AuthenticateAsync(command.Key, cancellationToken);
        return key.IsFailure ? key : await spv.RequestResultAsync(key.Value, command.RunId, command.RequestId, command.AnafRequestId, command.Error, cancellationToken);
    }
}

public sealed record FinishSpvRunCommand(string? Key, Guid RunId, string? Error) : ICommand;

internal sealed class FinishSpvRunCommandHandler(SpvService spv) : ICommandHandler<FinishSpvRunCommand>
{
    public async Task<Result> Handle(FinishSpvRunCommand command, CancellationToken cancellationToken)
    {
        Result<SpvAgentKey> key = await spv.AuthenticateAsync(command.Key, cancellationToken);
        return key.IsFailure ? key : await spv.FinishRunAsync(key.Value, command.RunId, command.Error, cancellationToken);
    }
}

/// <summary>Ce arată aplicația desktop: câți clienți urmărește și ultima trimitere reușită.</summary>
public sealed record SpvAgentStatus(string KeyName, int Pfas, DateTime? LastSuccessAtUtc, int QueuedRequests);

public sealed record GetSpvAgentStatusQuery(string? Key) : IQuery<SpvAgentStatus>;

internal sealed class GetSpvAgentStatusQueryHandler(IApplicationDbContext db, SpvService spv) : IQueryHandler<GetSpvAgentStatusQuery, SpvAgentStatus>
{
    public async Task<Result<SpvAgentStatus>> Handle(GetSpvAgentStatusQuery query, CancellationToken cancellationToken)
    {
        Result<SpvAgentKey> key = await spv.AuthenticateAsync(query.Key, cancellationToken);
        if (key.IsFailure)
        {
            return Result.Failure<SpvAgentStatus>(key.Error);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SpvAgentStatus(
            key.Value.Name,
            await db.PfaRegistrations.CountAsync(p => p.Cui != null && p.User.DeletedAtUtc == null, cancellationToken),
            await SpvStatusSupport.LastSuccessAsync(db, cancellationToken),
            await db.SpvRequests.CountAsync(r => r.Status == SpvRequestStatus.Queued, cancellationToken));
    }
}

internal static class SpvStatusSupport
{
    public static Task<DateTime?> LastSuccessAsync(IApplicationDbContext db, CancellationToken cancellationToken) =>
        db.SpvSyncRuns
            .Where(r => r.Status == SpvSyncRunStatus.Completed)
            .OrderByDescending(r => r.StartedAtUtc)
            .Select(r => r.FinishedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
}

// ---- web (admin și contabil) ----

public sealed record SpvMessageDto(
    Guid Id,
    string Type,
    DateTime CreatedAtUtc,
    string? Details,
    SpvMessageStatus Status,
    string? Note,
    bool HasDocument,
    bool Read,
    string? RequestType);

public sealed record SpvRequestDto(
    Guid Id,
    string Type,
    IReadOnlyDictionary<string, string> Parameters,
    SpvRequestStatus Status,
    DateTime CreatedAtUtc,
    DateTime? SentAtUtc,
    string? Error);

public sealed record PfaSpvDto(DateTime? LastSyncAtUtc, IReadOnlyList<SpvMessageDto> Messages, IReadOnlyList<SpvRequestDto> Requests);

public sealed record GetPfaSpvQuery(Guid PfaId, bool LinkedOnly = false) : IQuery<PfaSpvDto>;

internal sealed class GetPfaSpvQueryHandler(IApplicationDbContext db) : IQueryHandler<GetPfaSpvQuery, PfaSpvDto>
{
    public async Task<Result<PfaSpvDto>> Handle(GetPfaSpvQuery query, CancellationToken cancellationToken)
    {
        string? rawCui = await db.PfaRegistrations
            .Where(p => p.Id == query.PfaId)
            .Select(p => p.Cui)
            .SingleOrDefaultAsync(cancellationToken);
        if (rawCui is null && !await db.PfaRegistrations.AnyAsync(p => p.Id == query.PfaId, cancellationToken))
        {
            return Result.Failure<PfaSpvDto>(AccountingErrors.PfaNotFound);
        }

        string cif = new([.. (rawCui ?? string.Empty).Where(char.IsDigit)]);
        List<SpvRequest> requests = await db.SpvRequests.AsNoTracking()
            .Where(r => r.PfaRegistrationId == query.PfaId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var requestTypes = requests.ToDictionary(r => r.Id, r => r.Type);
        List<SpvMessage> messages = await db.SpvMessages.AsNoTracking()
            .Where(m => m.PfaRegistrationId == query.PfaId || !query.LinkedOnly && cif.Length > 0 && m.Cif == cif)
            .OrderByDescending(m => m.AnafCreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new PfaSpvDto(
            await SpvStatusSupport.LastSuccessAsync(db, cancellationToken),
            [.. messages.Select(m => new SpvMessageDto(
                m.Id, m.Type, m.AnafCreatedAtUtc, m.Details, m.Status, m.Note, m.DocumentId is not null, m.ReadAtUtc is not null,
                m.SpvRequestId is { } requestId ? requestTypes.GetValueOrDefault(requestId) : null))],
            [.. requests.Select(r => new SpvRequestDto(
                r.Id, r.Type, AccountingJson.Deserialize<Dictionary<string, string>>(r.ParametersJson, []) ?? [], r.Status, r.CreatedAtUtc, r.SentAtUtc, r.Error))]);
    }
}

public sealed record QueueSpvRequestCommand(Guid PfaId, string Type, IReadOnlyDictionary<string, string>? Parameters) : ICommand;

internal sealed class QueueSpvRequestCommandHandler(SpvService spv, IUserContext userContext) : ICommandHandler<QueueSpvRequestCommand>
{
    public async Task<Result> Handle(QueueSpvRequestCommand command, CancellationToken cancellationToken)
    {
        Result<SpvRequest> queued = await spv.QueueRequestAsync(command.PfaId, command.Type, command.Parameters, userContext.UserId, cancellationToken);
        return queued.IsFailure ? Result.Failure(queued.Error) : Result.Success();
    }
}

public sealed record MarkSpvMessageReadCommand(Guid MessageId) : ICommand;

internal sealed class MarkSpvMessageReadCommandHandler(IApplicationDbContext db) : ICommandHandler<MarkSpvMessageReadCommand>
{
    public async Task<Result> Handle(MarkSpvMessageReadCommand command, CancellationToken cancellationToken)
    {
        SpvMessage? message = await db.SpvMessages.SingleOrDefaultAsync(m => m.Id == command.MessageId, cancellationToken);
        if (message is null)
        {
            return Result.Failure(SpvErrors.MessageNotFound);
        }

        message.ReadAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record SpvFile(byte[] Content, string ContentType, string FileName);

public sealed record GetSpvMessageFileQuery(Guid MessageId) : IQuery<SpvFile>;

internal sealed class GetSpvMessageFileQueryHandler(IApplicationDbContext db, DeclarationFiles files) : IQueryHandler<GetSpvMessageFileQuery, SpvFile>
{
    public async Task<Result<SpvFile>> Handle(GetSpvMessageFileQuery query, CancellationToken cancellationToken)
    {
        Guid? documentId = await db.SpvMessages.Where(m => m.Id == query.MessageId).Select(m => m.DocumentId).SingleOrDefaultAsync(cancellationToken);
        return await files.ReadAsync(documentId, cancellationToken) is { } stored
            ? new SpvFile(stored.Content, stored.Document.ContentType, stored.Document.OriginalFileName)
            : Result.Failure<SpvFile>(SpvErrors.MessageNotFound);
    }
}

// ---- cheile aplicației (doar admin) ----

public sealed record SpvAgentKeyDto(Guid Id, string Name, string Prefix, DateTime CreatedAtUtc, DateTime? LastUsedAtUtc);

/// <summary>Cheia nouă, afișată o singură dată.</summary>
public sealed record CreatedSpvAgentKey(SpvAgentKeyDto Key, string Secret);

/// <summary>Aplicația desktop SPV, pentru admin: cheile active și ultimele trimiteri.</summary>
public sealed record SpvOverviewDto(IReadOnlyList<SpvAgentKeyDto> Keys, DateTime? LastSuccessAtUtc, string? LastError, int NeedsAttention, int QueuedRequests);

public sealed record GetSpvOverviewQuery : IQuery<SpvOverviewDto>;

internal sealed class GetSpvOverviewQueryHandler(IApplicationDbContext db) : IQueryHandler<GetSpvOverviewQuery, SpvOverviewDto>
{
    public async Task<Result<SpvOverviewDto>> Handle(GetSpvOverviewQuery query, CancellationToken cancellationToken)
    {
        List<SpvAgentKeyDto> keys = await db.SpvAgentKeys.AsNoTracking()
            .Where(k => k.RevokedAtUtc == null)
            .OrderBy(k => k.CreatedAtUtc)
            .Select(k => new SpvAgentKeyDto(k.Id, k.Name, k.Prefix, k.CreatedAtUtc, k.LastUsedAtUtc))
            .ToListAsync(cancellationToken);
        string? lastError = await db.SpvSyncRuns
            .Where(r => r.Status != SpvSyncRunStatus.Running)
            .OrderByDescending(r => r.StartedAtUtc)
            .Select(r => r.Error)
            .FirstOrDefaultAsync(cancellationToken);
        return new SpvOverviewDto(
            keys,
            await SpvStatusSupport.LastSuccessAsync(db, cancellationToken),
            lastError,
            await db.SpvMessages.CountAsync(m => m.Status == SpvMessageStatus.NeedsAttention, cancellationToken),
            await db.SpvRequests.CountAsync(r => r.Status == SpvRequestStatus.Queued, cancellationToken));
    }
}

public sealed record CreateSpvAgentKeyCommand(string? Name) : ICommand<CreatedSpvAgentKey>;

internal sealed class CreateSpvAgentKeyCommandHandler(SpvService spv, IUserContext userContext) : ICommandHandler<CreateSpvAgentKeyCommand, CreatedSpvAgentKey>
{
    public async Task<Result<CreatedSpvAgentKey>> Handle(CreateSpvAgentKeyCommand command, CancellationToken cancellationToken)
    {
        Result<(SpvAgentKey Key, string Secret)> created = await spv.CreateKeyAsync(userContext.UserId, command.Name, cancellationToken);
        if (created.IsFailure)
        {
            return Result.Failure<CreatedSpvAgentKey>(created.Error);
        }

        SpvAgentKey key = created.Value.Key;
        return new CreatedSpvAgentKey(new SpvAgentKeyDto(key.Id, key.Name, key.Prefix, key.CreatedAtUtc, key.LastUsedAtUtc), created.Value.Secret);
    }
}

public sealed record RevokeSpvAgentKeyCommand(Guid KeyId) : ICommand;

internal sealed class RevokeSpvAgentKeyCommandHandler(SpvService spv, IUserContext userContext) : ICommandHandler<RevokeSpvAgentKeyCommand>
{
    public async Task<Result> Handle(RevokeSpvAgentKeyCommand command, CancellationToken cancellationToken)
    {
        await spv.RevokeKeyAsync(command.KeyId, userContext.UserId, cancellationToken);
        return Result.Success();
    }
}
