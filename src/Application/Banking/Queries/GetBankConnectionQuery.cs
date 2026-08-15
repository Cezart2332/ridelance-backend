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

/// <param name="Candidates">
/// Populat doar când revendicarea a fost ambiguă: mai multe conexiuni noi, sau mai multe
/// conectări în curs în același timp. Utilizatorul alege una, iar noi nu ghicim.
/// </param>
public sealed record BankConnectionCandidate(
    string ProviderConnectionId,
    string? InstitutionName,
    string? InstitutionLogo,
    DateTime? CreatedAtUtc);

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
    DateTime? LinkExpiresAtUtc,
    List<BankConnectionCandidate> Candidates);

public sealed record GetBankConnectionQuery : IQuery<BankConnectionResponse?>;

/// <summary>
/// Starea conexiunii bancare a utilizatorului curent.
///
/// Aici se face și revendicarea: providerul nu ne anunță când cineva a terminat conectarea,
/// deci momentul în care aflăm e chiar întrebarea pe care o pune pagina în timp ce așteaptă.
/// </summary>
internal sealed class GetBankConnectionQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    BankConnectionClaimService claimService)
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

        List<BankConnectionCandidate> candidates = [];

        if (connection.Status is BankConnectionStatus.Created or BankConnectionStatus.Pending)
        {
            BankClaimOutcome outcome = await claimService.TryClaimAsync(connection, cancellationToken);
            candidates = [.. outcome.Candidates.Select(c => new BankConnectionCandidate(
                c.Id,
                c.InstitutionName,
                c.InstitutionLogo,
                c.CreatedAtUtc))];

            if (outcome.Status == BankConnectionStatus.Linked)
            {
                // Revendicarea tocmai a scris conturile; le recitim ca răspunsul să le conțină.
                connection.Accounts = await context.BankAccounts
                    .Where(a => a.BankConnectionId == connection.Id)
                    .ToListAsync(cancellationToken);
            }
        }

        return Result.Success<BankConnectionResponse?>(MapResponse(connection, candidates));
    }

    internal static BankConnectionResponse MapResponse(
        BankConnection connection,
        List<BankConnectionCandidate>? candidates = null) =>
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
            connection.LinkExpiresAtUtc,
            candidates ?? []);
}
