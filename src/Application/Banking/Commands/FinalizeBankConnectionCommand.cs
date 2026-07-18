using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Banking.Queries;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Banking.Commands;

public sealed record FinalizeBankConnectionCommand(string Reference)
    : ICommand<BankConnectionResponse>;

internal sealed class FinalizeBankConnectionCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    BankConnectionFinalizer finalizer)
    : ICommandHandler<FinalizeBankConnectionCommand, BankConnectionResponse>
{
    public async Task<Result<BankConnectionResponse>> Handle(
        FinalizeBankConnectionCommand command,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        // Scoped pe user + reference: un "ref" scurs/ghicit e inutil pentru alt cont.
        BankConnection? connection = await context.BankConnections
            .Include(bc => bc.Accounts)
            .FirstOrDefaultAsync(
                bc => bc.UserId == userId && bc.Reference == command.Reference,
                cancellationToken);

        if (connection is null)
        {
            return Result.Failure<BankConnectionResponse>(Error.NotFound(
                "Bank.ConnectionNotFound",
                "Conexiunea bancară nu a fost găsită."));
        }

        if (connection.Status is BankConnectionStatus.Linked or BankConnectionStatus.Revoked)
        {
            // Idempotent — a doua chemare nu mai lovește providerul.
            return GetBankConnectionQueryHandler.MapResponse(connection);
        }

        try
        {
            await finalizer.FinalizeAsync(connection, cancellationToken);
        }
        catch (BankDataProviderException ex)
        {
            return Result.Failure<BankConnectionResponse>(
                Error.Problem("Bank.ProviderError", ex.Message));
        }

        return GetBankConnectionQueryHandler.MapResponse(connection);
    }
}
