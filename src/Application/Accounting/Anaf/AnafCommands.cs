using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Application.Accounting.Anaf;

/// <summary>Conexiunea ANAF a împuternicitului, cum o vede adminul.</summary>
public sealed record AnafConnectionDto(
    bool Configured,
    AnafConnectionStatus? Status,
    string? ConnectedBy,
    DateTime? ConnectedAtUtc,
    DateTime? AccessExpiresAtUtc,
    DateTime? RefreshExpiresAtUtc,
    string? LastError);

public sealed record AnafPfaLinkDto(AnafPfaLinkStatus Status, DateTime EnabledAtUtc, DateTime? LastSyncAtUtc, string? LastError);

public sealed record EFacturaMessageDto(
    Guid Id,
    EFacturaMessageKind Kind,
    string AnafType,
    DateTime CreatedAtUtc,
    string? InvoiceNumber,
    DateOnly? IssueDate,
    string? SupplierName,
    string? SupplierCif,
    string? CustomerName,
    string? CustomerCif,
    string? Currency,
    decimal? TotalAmount,
    decimal? VatAmount,
    bool Downloaded,
    string? DownloadError,
    string? Details);

/// <summary>Tabul „ANAF” din fișa clientului: conexiunea, legătura PFA-ului și mesajele e-Factura.</summary>
public sealed record PfaEFacturaDto(AnafConnectionDto Connection, AnafPfaLinkDto? Link, IReadOnlyList<EFacturaMessageDto> Messages);

public sealed record AnafFile(byte[] Content, string ContentType, string FileName);

internal static class AnafDtos
{
    public static async Task<AnafConnectionDto> ConnectionAsync(IApplicationDbContext db, AnafEFacturaService service, CancellationToken cancellationToken)
    {
        AnafConnection? connection = await service.ActiveConnectionAsync(cancellationToken);
        if (connection is null)
        {
            return new AnafConnectionDto(service.IsConfigured, null, null, null, null, null, null);
        }

        string? by = await db.Users.Where(u => u.Id == connection.UserId).Select(u => (u.FirstName + " " + u.LastName).Trim()).SingleOrDefaultAsync(cancellationToken);
        return new AnafConnectionDto(
            service.IsConfigured,
            connection.Status,
            by,
            connection.ConnectedAtUtc,
            connection.AccessExpiresAtUtc,
            connection.RefreshExpiresAtUtc,
            connection.LastError);
    }
}

public sealed record StartAnafAuthorizationCommand(string? ReturnPath) : ICommand<string>;

internal sealed class StartAnafAuthorizationCommandHandler(AnafEFacturaService service, IUserContext userContext)
    : ICommandHandler<StartAnafAuthorizationCommand, string>
{
    public Task<Result<string>> Handle(StartAnafAuthorizationCommand command, CancellationToken cancellationToken) =>
        service.StartAsync(userContext.UserId, command.ReturnPath, cancellationToken);
}

/// <summary>Callback-ul ANAF (fără utilizator logat: îl identifică autorizarea pornită din aplicație).</summary>
public sealed record CompleteAnafAuthorizationCommand(string? Code, string? State, string? Error) : ICommand<AnafAuthorizationOutcome>;

/// <param name="Error">Mesajul pentru admin; <c>null</c> = conectat.</param>
public sealed record AnafAuthorizationOutcome(string ReturnPath, string? Error);

internal sealed class CompleteAnafAuthorizationCommandHandler(AnafEFacturaService service)
    : ICommandHandler<CompleteAnafAuthorizationCommand, AnafAuthorizationOutcome>
{
    public async Task<Result<AnafAuthorizationOutcome>> Handle(CompleteAnafAuthorizationCommand command, CancellationToken cancellationToken)
    {
        (string path, Result outcome) = await service.CompleteAsync(command.Code, command.State, command.Error, cancellationToken);
        return new AnafAuthorizationOutcome(path, outcome.IsFailure ? outcome.Error.Description : null);
    }
}

public sealed record GetAnafConnectionQuery : IQuery<AnafConnectionDto>;

internal sealed class GetAnafConnectionQueryHandler(IApplicationDbContext db, AnafEFacturaService service)
    : IQueryHandler<GetAnafConnectionQuery, AnafConnectionDto>
{
    public async Task<Result<AnafConnectionDto>> Handle(GetAnafConnectionQuery query, CancellationToken cancellationToken) =>
        await AnafDtos.ConnectionAsync(db, service, cancellationToken);
}

public sealed record DisconnectAnafCommand : ICommand;

internal sealed class DisconnectAnafCommandHandler(AnafEFacturaService service, IUserContext userContext) : ICommandHandler<DisconnectAnafCommand>
{
    public async Task<Result> Handle(DisconnectAnafCommand command, CancellationToken cancellationToken)
    {
        await service.DisconnectAsync(userContext.UserId, cancellationToken);
        return Result.Success();
    }
}

public sealed record GetPfaEFacturaQuery(Guid PfaId) : IQuery<PfaEFacturaDto>;

internal sealed class GetPfaEFacturaQueryHandler(IApplicationDbContext db, AnafEFacturaService service)
    : IQueryHandler<GetPfaEFacturaQuery, PfaEFacturaDto>
{
    public async Task<Result<PfaEFacturaDto>> Handle(GetPfaEFacturaQuery query, CancellationToken cancellationToken)
    {
        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == query.PfaId, cancellationToken))
        {
            return Result.Failure<PfaEFacturaDto>(AccountingErrors.PfaNotFound);
        }

        AnafPfaLink? link = await db.AnafPfaLinks.AsNoTracking().SingleOrDefaultAsync(l => l.PfaRegistrationId == query.PfaId, cancellationToken);
        List<EFacturaMessageDto> messages = await db.EFacturaMessages
            .AsNoTracking()
            .Where(m => m.PfaRegistrationId == query.PfaId)
            .OrderByDescending(m => m.AnafCreatedAtUtc)
            .Select(m => new EFacturaMessageDto(
                m.Id, m.Kind, m.AnafType, m.AnafCreatedAtUtc, m.InvoiceNumber, m.IssueDate, m.SupplierName, m.SupplierCif,
                m.CustomerName, m.CustomerCif, m.Currency, m.TotalAmount, m.VatAmount, m.ZipDocumentId != null, m.DownloadError, m.Details))
            .ToListAsync(cancellationToken);

        return new PfaEFacturaDto(
            await AnafDtos.ConnectionAsync(db, service, cancellationToken),
            link is null ? null : new AnafPfaLinkDto(link.Status, link.EnabledAtUtc, link.LastSyncAtUtc, link.LastError),
            messages);
    }
}

public sealed record ConnectPfaEFacturaCommand(Guid PfaId) : ICommand;

internal sealed class ConnectPfaEFacturaCommandHandler(AnafEFacturaService service, IUserContext userContext) : ICommandHandler<ConnectPfaEFacturaCommand>
{
    public async Task<Result> Handle(ConnectPfaEFacturaCommand command, CancellationToken cancellationToken)
    {
        Result<AnafPfaLink> link = await service.ConnectPfaAsync(command.PfaId, userContext.UserId, cancellationToken);
        return link.IsFailure ? Result.Failure(link.Error) : Result.Success();
    }
}

public sealed record SyncPfaEFacturaCommand(Guid PfaId) : ICommand<EFacturaSyncResult>;

internal sealed class SyncPfaEFacturaCommandHandler(AnafEFacturaService service) : ICommandHandler<SyncPfaEFacturaCommand, EFacturaSyncResult>
{
    public Task<Result<EFacturaSyncResult>> Handle(SyncPfaEFacturaCommand command, CancellationToken cancellationToken) =>
        service.SyncAsync(command.PfaId, cancellationToken);
}

public sealed record DisablePfaEFacturaCommand(Guid PfaId) : ICommand;

internal sealed class DisablePfaEFacturaCommandHandler(AnafEFacturaService service, IUserContext userContext) : ICommandHandler<DisablePfaEFacturaCommand>
{
    public async Task<Result> Handle(DisablePfaEFacturaCommand command, CancellationToken cancellationToken)
    {
        await service.DisablePfaAsync(command.PfaId, userContext.UserId, cancellationToken);
        return Result.Success();
    }
}

public enum EFacturaFileKind
{
    Xml,
    Pdf,
}

public sealed record GetEFacturaFileQuery(Guid MessageId, EFacturaFileKind Kind) : IQuery<AnafFile>;

internal sealed class GetEFacturaFileQueryHandler(AnafEFacturaService service) : IQueryHandler<GetEFacturaFileQuery, AnafFile>
{
    public async Task<Result<AnafFile>> Handle(GetEFacturaFileQuery query, CancellationToken cancellationToken)
    {
        Result<(byte[] Content, string FileName)> file = query.Kind == EFacturaFileKind.Pdf
            ? await service.PdfAsync(query.MessageId, cancellationToken)
            : await service.XmlAsync(query.MessageId, cancellationToken);
        if (file.IsFailure)
        {
            return Result.Failure<AnafFile>(file.Error);
        }

        string contentType = query.Kind == EFacturaFileKind.Pdf ? "application/pdf" : "application/xml";
        return new AnafFile(file.Value.Content, contentType, file.Value.FileName);
    }
}

/// <summary>
/// Un pas al jobului de fundal: reînnoiește tokenul ANAF și sincronizează clienții nesincronizați
/// de 12 ore. Fără conexiune ANAF activă nu face nimic.
/// </summary>
public sealed record RunEFacturaSyncPassCommand : ICommand<int>;

internal sealed class RunEFacturaSyncPassCommandHandler(IApplicationDbContext db, AnafEFacturaService service, ILogger<RunEFacturaSyncPassCommandHandler> logger)
    : ICommandHandler<RunEFacturaSyncPassCommand, int>
{
    private static readonly TimeSpan MinTimeBetweenSyncs = TimeSpan.FromHours(12);

    public async Task<Result<int>> Handle(RunEFacturaSyncPassCommand command, CancellationToken cancellationToken)
    {
        if (!service.IsConfigured || await service.ActiveConnectionAsync(cancellationToken) is not { Status: AnafConnectionStatus.Active })
        {
            return 0;
        }

        // Reînnoirea tokenului, chiar dacă niciun client nu e de sincronizat acum.
        Result<string> token = await service.AccessTokenAsync(cancellationToken);
        if (token.IsFailure)
        {
            return Result.Failure<int>(token.Error);
        }

        DateTime due = DateTime.UtcNow - MinTimeBetweenSyncs;
        List<Guid> pfas = await db.AnafPfaLinks
            .Where(l => l.Status == AnafPfaLinkStatus.Active && (l.LastSyncAtUtc == null || l.LastSyncAtUtc < due))
            .Select(l => l.PfaRegistrationId)
            .ToListAsync(cancellationToken);
        int synced = 0;
        foreach (Guid pfaId in pfas)
        {
            Result<EFacturaSyncResult> result = await service.SyncAsync(pfaId, cancellationToken);
            if (result.IsFailure)
            {
                logger.LogWarning("e-Factura: PFA {PfaId}: {Error}", pfaId, result.Error.Description);
                continue;
            }

            synced++;
        }

        return synced;
    }
}
