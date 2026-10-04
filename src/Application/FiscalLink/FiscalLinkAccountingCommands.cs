using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Accounting.Ledger;
using Domain.FiscalLink;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.FiscalLink;

public sealed record FiscalLinkAccountingSyncDto(bool Configured, DateTime? LastAttemptAtUtc, DateTime? LastSyncAtUtc,
    string? Error, int Receipts, int ZReports);

public sealed record GetFiscalLinkAccountingSyncQuery : IQuery<FiscalLinkAccountingSyncDto>;
public sealed record SyncFiscalLinkAccountingCommand : ICommand<FiscalLinkAccountingSyncDto>;

internal static class FiscalLinkAccountingAccess
{
    public static async Task<FiscalLinkClient?> OwnAsync(IApplicationDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        Guid? pfaId = await db.PfaRegistrations.Where(p => p.UserId == userId && p.User.DeletedAtUtc == null)
            .OrderByDescending(p => p.CreatedAtUtc).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        return pfaId is null ? null : await db.FiscalLinkClients.SingleOrDefaultAsync(
            c => c.PfaRegistrationId == pfaId && c.UserId == userId, cancellationToken);
    }

    public static async Task<FiscalLinkAccountingSyncDto> StatusAsync(IApplicationDbContext db, FiscalLinkClient? client,
        bool configured, CancellationToken cancellationToken) => new(configured, client?.LastSyncAttemptAtUtc,
        client?.LastSyncAtUtc, client?.LastSyncError,
        client is null ? 0 : await db.FiscalReceipts.CountAsync(r => r.PfaRegistrationId == client.PfaRegistrationId, cancellationToken),
        client is null ? 0 : await db.ZReports.CountAsync(z => z.PfaRegistrationId == client.PfaRegistrationId && z.RegisterSerial != null, cancellationToken));
}

internal sealed class GetFiscalLinkAccountingSyncQueryHandler(IApplicationDbContext db, IUserContext user, IFiscalLinkAccountingService fiscalLink)
    : IQueryHandler<GetFiscalLinkAccountingSyncQuery, FiscalLinkAccountingSyncDto>
{
    public async Task<Result<FiscalLinkAccountingSyncDto>> Handle(GetFiscalLinkAccountingSyncQuery query, CancellationToken cancellationToken) =>
        await FiscalLinkAccountingAccess.StatusAsync(db, await FiscalLinkAccountingAccess.OwnAsync(db, user.UserId, cancellationToken), fiscalLink.IsConfigured, cancellationToken);
}

internal sealed class SyncFiscalLinkAccountingCommandHandler(IApplicationDbContext db, IUserContext user, IFiscalLinkAccountingService fiscalLink,
    ICommandHandler<RunLedgerImportCommand, IReadOnlyList<LedgerImportResult>> import)
    : ICommandHandler<SyncFiscalLinkAccountingCommand, FiscalLinkAccountingSyncDto>
{
    public async Task<Result<FiscalLinkAccountingSyncDto>> Handle(SyncFiscalLinkAccountingCommand command, CancellationToken cancellationToken)
    {
        FiscalLinkClient? client = await FiscalLinkAccountingAccess.OwnAsync(db, user.UserId, cancellationToken);
        if (client is null)
        {
            return Result.Failure<FiscalLinkAccountingSyncDto>(Error.Problem("FiscalLink.NotConnected", "Conectează mai întâi casa de marcat la FiscalLink."));
        }
        Result<IReadOnlyList<LedgerImportResult>> result = await import.Handle(new RunLedgerImportCommand(client.PfaRegistrationId, FiscalLinkOnly: true), cancellationToken);
        return result.IsFailure ? Result.Failure<FiscalLinkAccountingSyncDto>(result.Error)
            : await FiscalLinkAccountingAccess.StatusAsync(db, client, fiscalLink.IsConfigured, cancellationToken);
    }
}
