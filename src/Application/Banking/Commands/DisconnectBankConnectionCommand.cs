using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Banking.Commands;

public sealed record DisconnectBankConnectionCommand : ICommand<bool>;

internal sealed class DisconnectBankConnectionCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IBankDataProvider provider,
    ISecretProtector secretProtector)
    : ICommandHandler<DisconnectBankConnectionCommand, bool>
{
    public async Task<Result<bool>> Handle(
        DisconnectBankConnectionCommand command,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        BankConnection? connection = await context.BankConnections
            .Include(bc => bc.Accounts)
            .FirstOrDefaultAsync(bc => bc.UserId == userId, cancellationToken);

        if (connection is null)
        {
            return Result.Failure<bool>(Error.NotFound(
                "Bank.ConnectionNotFound",
                "Nu există nicio bancă conectată."));
        }

        // Revocăm accesul la provider (best-effort — poate fi deja expirat).
        if (!string.IsNullOrEmpty(connection.ProviderRequisitionId))
        {
            try
            {
                await provider.DeleteConnectionAsync(
                    secretProtector.Unprotect(connection.ProviderRequisitionId),
                    cancellationToken);
            }
            catch (BankDataProviderException)
            {
                // Ignorăm — deconectarea locală rămâne valabilă.
            }
        }

        connection.Status = BankConnectionStatus.Revoked;
        connection.ErrorMessage = null;

        // Tranzacțiile istorice rămân — doar sync-ul se oprește.
        foreach (Domain.Banking.BankAccount account in connection.Accounts)
        {
            account.IsActive = false;
        }

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
