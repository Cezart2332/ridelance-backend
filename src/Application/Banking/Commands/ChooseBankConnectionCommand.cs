using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Banking.Queries;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Banking.Commands;

/// <summary>
/// Alegerea explicită a conexiunii, când revendicarea automată a refuzat să ghicească.
///
/// Se ajunge aici doar în cazul ambiguu: două conexiuni noi, sau două conectări în curs în
/// același timp. Utilizatorul vede candidații (bancă și moment) și spune care e al lui.
/// </summary>
public sealed record ChooseBankConnectionCommand(string ProviderConnectionId)
    : ICommand<BankConnectionResponse>;

internal sealed class ChooseBankConnectionCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    BankConnectionClaimService claimService)
    : ICommandHandler<ChooseBankConnectionCommand, BankConnectionResponse>
{
    public async Task<Result<BankConnectionResponse>> Handle(
        ChooseBankConnectionCommand command,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        BankConnection? connection = await context.BankConnections
            .Include(bc => bc.Accounts)
            .FirstOrDefaultAsync(bc => bc.UserId == userId, cancellationToken);

        if (connection is null)
        {
            return Result.Failure<BankConnectionResponse>(BankErrors.NoConnection);
        }

        BankClaimOutcome outcome = await claimService.ClaimChosenAsync(
            connection,
            command.ProviderConnectionId,
            cancellationToken);

        if (outcome.Status != BankConnectionStatus.Linked)
        {
            return Result.Failure<BankConnectionResponse>(Error.Problem(
                "Bank.ChoiceUnavailable",
                "Conexiunea aleasă nu mai este disponibilă. Reîmprospătează lista și încearcă din nou."));
        }

        connection.Accounts = await context.BankAccounts
            .Where(a => a.BankConnectionId == connection.Id)
            .ToListAsync(cancellationToken);

        return GetBankConnectionQueryHandler.MapResponse(connection);
    }
}
