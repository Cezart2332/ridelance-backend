using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Banking.Queries;

public sealed record BankAccountResponse(
    string? IbanMasked,
    string? Currency,
    string? OwnerName);

public sealed record BankConnectionResponse(
    string Status,
    string InstitutionId,
    string InstitutionName,
    string? InstitutionLogo,
    DateTime? ConsentExpiresAtUtc,
    DateTime? LinkedAtUtc,
    DateTime? LastSyncedAtUtc,
    string? ErrorMessage,
    List<BankAccountResponse> Accounts);

public sealed record GetBankConnectionQuery : IQuery<BankConnectionResponse?>;

internal sealed class GetBankConnectionQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    BankConnectionFinalizer finalizer)
    : IQueryHandler<GetBankConnectionQuery, BankConnectionResponse?>
{
    public async Task<Result<BankConnectionResponse?>> Handle(
        GetBankConnectionQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        BankConnection? connection = await context.BankConnections
            .Include(bc => bc.Accounts)
            .FirstOrDefaultAsync(bc => bc.UserId == userId, cancellationToken);

        if (connection is null)
        {
            return Result.Success<BankConnectionResponse?>(null);
        }

        // Finalizare oportunistă: acoperă userul care a autorizat la bancă dar a
        // pierdut redirectul cu ?ref= (ex. sesiune expirată la întoarcere).
        bool worthChecking =
            connection.Status is BankConnectionStatus.Created or BankConnectionStatus.Pending &&
            connection.CreatedAtUtc > DateTime.UtcNow.AddDays(-2);

        if (worthChecking)
        {
            try
            {
                await finalizer.FinalizeAsync(connection, cancellationToken);
            }
            catch (BankDataProviderException)
            {
                // Starea curentă din DB rămâne valabilă.
            }
        }

        return Result.Success<BankConnectionResponse?>(MapResponse(connection));
    }

    internal static BankConnectionResponse MapResponse(BankConnection connection) =>
        new(
            connection.Status.ToString(),
            connection.InstitutionId,
            connection.InstitutionName,
            connection.InstitutionLogoUrl,
            connection.ConsentExpiresAtUtc,
            connection.LinkedAtUtc,
            connection.LastSyncedAtUtc,
            connection.ErrorMessage,
            [.. connection.Accounts
                .Where(a => a.IsActive)
                .Select(a => new BankAccountResponse(a.IbanMasked, a.Currency, a.OwnerName))]);
}
