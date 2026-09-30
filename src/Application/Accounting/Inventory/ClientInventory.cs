using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Ledger;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Inventory;

// Inventarul văzut de PFA (spec registre §5 pas 2, §8): „RIDElance a identificat X elemente”. Fiecare
// acțiune lucrează pe PFA-ul utilizatorului logat, niciodată pe un id primit.

/// <summary><c>GET /pfa/inventory</c> — inventarierea de confirmat, altfel ultima finală.</summary>
public sealed record GetClientInventoryQuery : IQuery<InventoryCountDto?>;

internal sealed class GetClientInventoryQueryHandler(IApplicationDbContext db, IUserContext userContext)
    : IQueryHandler<GetClientInventoryQuery, InventoryCountDto?>
{
    public async Task<Result<InventoryCountDto?>> Handle(GetClientInventoryQuery query, CancellationToken cancellationToken)
    {
        if (await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is not { } pfaId)
        {
            return Result.Failure<InventoryCountDto?>(ClientLedger.NoPfa);
        }

        InventoryCount? count = await db.InventoryCounts.AsNoTracking()
            .Include(c => c.Items)
            .Where(c => c.PfaRegistrationId == pfaId)
            .OrderBy(c => c.Status == InventoryStatus.AwaitingPfaConfirmation ? 0 : 1)
            .ThenByDescending(c => c.Date)
            .FirstOrDefaultAsync(cancellationToken);
        return count is null ? Result.Success<InventoryCountDto?>(null) : await InventorySupport.DtoAsync(db, count, cancellationToken);
    }
}

public sealed record ClientInventoryItemCommand(Guid CountId, Guid ItemId, InventoryItemAction Action, decimal? Value, string? Note) : ICommand<InventoryCountDto>;

internal sealed class ClientInventoryItemCommandHandler(IApplicationDbContext db, IUserContext userContext, ICommandHandler<UpdateInventoryItemCommand, InventoryCountDto> update)
    : ICommandHandler<ClientInventoryItemCommand, InventoryCountDto>
{
    public async Task<Result<InventoryCountDto>> Handle(ClientInventoryItemCommand command, CancellationToken cancellationToken) =>
        await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is { } pfaId
            ? await update.Handle(new UpdateInventoryItemCommand(pfaId, command.CountId, command.ItemId, command.Action, command.Value, command.Note, ByAdmin: false), cancellationToken)
            : Result.Failure<InventoryCountDto>(ClientLedger.NoPfa);
}

public sealed record ClientAddInventoryItemCommand(Guid CountId, InventoryCategory Category, string Description, decimal Value, string? Note) : ICommand<InventoryCountDto>;

internal sealed class ClientAddInventoryItemCommandHandler(IApplicationDbContext db, IUserContext userContext, ICommandHandler<AddInventoryItemCommand, InventoryCountDto> add)
    : ICommandHandler<ClientAddInventoryItemCommand, InventoryCountDto>
{
    public async Task<Result<InventoryCountDto>> Handle(ClientAddInventoryItemCommand command, CancellationToken cancellationToken) =>
        await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is { } pfaId
            ? await add.Handle(new AddInventoryItemCommand(pfaId, command.CountId, command.Category, command.Description, command.Value, command.Note, ByAdmin: false), cancellationToken)
            : Result.Failure<InventoryCountDto>(ClientLedger.NoPfa);
}

public sealed record ClientSubmitInventoryCommand(Guid CountId) : ICommand<InventoryCountDto>;

internal sealed class ClientSubmitInventoryCommandHandler(IApplicationDbContext db, IUserContext userContext, ICommandHandler<SubmitInventoryCountCommand, InventoryCountDto> submit)
    : ICommandHandler<ClientSubmitInventoryCommand, InventoryCountDto>
{
    public async Task<Result<InventoryCountDto>> Handle(ClientSubmitInventoryCommand command, CancellationToken cancellationToken) =>
        await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is { } pfaId
            ? await submit.Handle(new SubmitInventoryCountCommand(pfaId, command.CountId), cancellationToken)
            : Result.Failure<InventoryCountDto>(ClientLedger.NoPfa);
}
