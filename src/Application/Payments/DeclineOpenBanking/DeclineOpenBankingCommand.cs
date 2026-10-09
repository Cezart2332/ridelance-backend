using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Banking.Commands;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Payments.DeclineOpenBanking;

/// <summary>
/// <c>POST /payments/subscription/open-banking/decline</c> — PFAlone nu plătește Open Banking după
/// luna gratuită: banca se deconectează, iar dashboardul nu-l mai întreabă.
/// </summary>
public sealed record DeclineOpenBankingCommand : ICommand;

internal sealed class DeclineOpenBankingCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    ICommandHandler<DisconnectBankConnectionCommand, bool> disconnect)
    : ICommandHandler<DeclineOpenBankingCommand>
{
    public static readonly Error NotOffered = Error.Problem(
        "OpenBanking.NotOffered",
        "Open Banking nu are de ales pe abonamentul tău.");

    public async Task<Result> Handle(DeclineOpenBankingCommand command, CancellationToken cancellationToken)
    {
        UserSubscription? subscription = await db.UserSubscriptions
            .Where(s => s.UserId == userContext.UserId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.ActivePendingBilling))
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null || subscription.Plan != SubscriptionPlan.PfaAlone || subscription.HasOpenBankingAddon)
        {
            return Result.Failure(NotOffered);
        }

        subscription.OpenBankingDeclinedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // Fără bancă conectată nu e nimic de oprit; refuzul rămâne înregistrat oricum.
        await disconnect.Handle(new DisconnectBankConnectionCommand(), cancellationToken);
        return Result.Success();
    }
}
