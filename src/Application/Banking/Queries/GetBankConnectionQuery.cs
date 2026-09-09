using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
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
    List<BankAccountResponse> Accounts,
    DateTime? LinkExpiresAtUtc);

public sealed record GetBankConnectionQuery : IQuery<BankConnectionResponse?>;

/// <summary>
/// Starea conexiunii bancare a utilizatorului curent.
///
/// Aici se face și finalizarea: furnizorul nu ne sună înapoi când cineva termină autorizarea la
/// bancă, deci momentul în care aflăm e chiar întrebarea pe care o pune pagina cât timp așteaptă.
/// </summary>
internal sealed class GetBankConnectionQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    BankConsentFinalizer finalizer)
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

        if (connection.Status is BankConnectionStatus.Created or BankConnectionStatus.Pending)
        {
            BankConnectionStatus status = await finalizer.TryFinalizeAsync(connection, cancellationToken);

            if (status == BankConnectionStatus.Linked)
            {
                // Finalizarea tocmai a scris conturile; le recitim ca răspunsul să le conțină.
                connection.Accounts = await context.BankAccounts
                    .Where(a => a.BankConnectionId == connection.Id)
                    .ToListAsync(cancellationToken);
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
                .Select(a => new BankAccountResponse(a.IbanMasked, a.Currency, a.OwnerName))],
            connection.LinkExpiresAtUtc);
}
