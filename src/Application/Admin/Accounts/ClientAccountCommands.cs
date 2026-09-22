using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Cars;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Admin.Accounts;

/// <summary>Starea unui cont după închidere sau redeschidere.</summary>
public sealed record ClientAccountStatusResponse(Guid UserId, DateTime? DeletedAtUtc, string? DeletionReason);

/// <summary>
/// Închide contul unui client — PFA sau SRL — fără să șteargă nimic.
///
/// Ce se schimbă: contul nu se mai poate autentifica, abonamentul se oprește în Stripe (un cont
/// închis nu are voie să mai fie taxat), iar anunțurile publicate ale unei firme se retrag din
/// marketplace. Ce rămâne: tot restul — dosarul, documentele, plățile, facturile, închirierile —
/// pentru contabilitate, pentru obligațiile legale și ca istoricul clientului să se vadă în admin.
/// </summary>
public sealed record CloseClientAccountCommand(Guid UserId, string? Reason) : ICommand<ClientAccountStatusResponse>;

/// <summary>
/// Redeschide un cont închis. Accesul revine; abonamentul nu — oprit în Stripe, se alege din nou,
/// iar anunțurile retrase se publică din nou de către firmă.
/// </summary>
public sealed record ReopenClientAccountCommand(Guid UserId) : ICommand<ClientAccountStatusResponse>;

internal static class ClientAccounts
{
    public static readonly Error Forbidden = Error.Failure("Admin.Forbidden", "Doar administratorii pot închide sau redeschide conturi.");

    public static readonly Error StripeCancelFailed = Error.Problem(
        "Accounts.StripeCancelFailed",
        "Nu am putut opri abonamentul în Stripe, deci contul a rămas deschis. Încearcă din nou sau oprește abonamentul din Stripe.");

    public static async Task<bool> IsAdminAsync(IApplicationDbContext context, Guid userId, CancellationToken cancellationToken) =>
        await context.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Role == UserRole.Admin, cancellationToken);

    /// <summary>Urma în jurnalul dosarului PFA, acolo unde adminul citește istoricul clientului.</summary>
    public static async Task LogOnPfaAsync(
        IApplicationDbContext context,
        Guid clientUserId,
        Guid adminUserId,
        string activityType,
        string description,
        CancellationToken cancellationToken)
    {
        Guid? pfaId = await context.PfaRegistrations
            .Where(p => p.UserId == clientUserId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (pfaId is Guid id)
        {
            context.PfaActivityLogs.Add(new PfaActivityLog
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = id,
                ActivityType = activityType,
                Description = description,
                CreatedAtUtc = DateTime.UtcNow,
                PerformedByUserId = adminUserId,
            });
        }
    }
}

internal sealed class CloseClientAccountCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IStripeService stripe)
    : ICommandHandler<CloseClientAccountCommand, ClientAccountStatusResponse>
{
    private static readonly SubscriptionStatus[] Billable =
    [
        SubscriptionStatus.Active,
        SubscriptionStatus.ActivePendingBilling,
        SubscriptionStatus.PaidPendingAccess,
        SubscriptionStatus.PastDue,
    ];

    public async Task<Result<ClientAccountStatusResponse>> Handle(
        CloseClientAccountCommand command,
        CancellationToken cancellationToken)
    {
        if (!await ClientAccounts.IsAdminAsync(context, userContext.UserId, cancellationToken))
        {
            return Result.Failure<ClientAccountStatusResponse>(ClientAccounts.Forbidden);
        }

        User? user = await context.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<ClientAccountStatusResponse>(UserErrors.NotFound(command.UserId));
        }

        if (user.Role is not (UserRole.Client or UserRole.CarPoster))
        {
            return Result.Failure<ClientAccountStatusResponse>(UserErrors.CannotCloseStaffAccount);
        }

        if (user.IsDeleted)
        {
            return new ClientAccountStatusResponse(user.Id, user.DeletedAtUtc, user.DeletionReason);
        }

        DateTime now = DateTime.UtcNow;

        // Întâi Stripe: dacă oprirea eșuează, contul rămâne deschis. Invers am avea un cont închis
        // care se taxează în continuare.
        List<UserSubscription> billable = await context.UserSubscriptions
            .Where(s => s.UserId == user.Id && Billable.Contains(s.Status))
            .ToListAsync(cancellationToken);

        foreach (UserSubscription subscription in billable)
        {
            if (!string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
            {
                try
                {
                    await stripe.CancelSubscriptionAsync(subscription.StripeSubscriptionId, cancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    return Result.Failure<ClientAccountStatusResponse>(ClientAccounts.StripeCancelFailed);
                }
            }

            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.CancelledAtUtc = now;
        }

        // Și anunțurile extra au abonament propriu, per mașină: se opresc la fel.
        List<Car> paidCars = await context.Cars
            .Where(c => c.PostedByUserId == user.Id
                && c.StripeSubscriptionId != null
                && (c.PaymentStatus == CarListingPaymentStatus.Paid || c.PaymentStatus == CarListingPaymentStatus.PastDue))
            .ToListAsync(cancellationToken);

        foreach (Car car in paidCars)
        {
            try
            {
                await stripe.CancelSubscriptionAsync(car.StripeSubscriptionId!, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return Result.Failure<ClientAccountStatusResponse>(ClientAccounts.StripeCancelFailed);
            }

            car.PaymentStatus = CarListingPaymentStatus.Cancelled;
        }

        // Anunțurile unei firme închise nu mai au ce căuta în marketplace. Pauză, nu arhivare:
        // la redeschidere firma le republică dintr-un click, cu tot istoricul lor.
        List<Car> published = await context.Cars
            .Where(c => c.PostedByUserId == user.Id && c.ListingStatus == ListingStatus.Published)
            .ToListAsync(cancellationToken);
        foreach (Car car in published)
        {
            car.ListingStatus = ListingStatus.Paused;
            car.UpdatedAtUtc = now;
        }

        string? reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
        user.DeletedAtUtc = now;
        user.DeletedByUserId = userContext.UserId;
        user.DeletionReason = reason;
        // Sesiunea deschisă se stinge la următoarea reînnoire.
        user.RefreshToken = null;
        user.RefreshTokenExpiryUtc = null;

        await ClientAccounts.LogOnPfaAsync(
            context,
            user.Id,
            userContext.UserId,
            "AccountClosed",
            reason is null ? "Cont închis. Datele rămân păstrate." : $"Cont închis. Motiv: {reason}",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return new ClientAccountStatusResponse(user.Id, user.DeletedAtUtc, user.DeletionReason);
    }
}

internal sealed class ReopenClientAccountCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<ReopenClientAccountCommand, ClientAccountStatusResponse>
{
    public async Task<Result<ClientAccountStatusResponse>> Handle(
        ReopenClientAccountCommand command,
        CancellationToken cancellationToken)
    {
        if (!await ClientAccounts.IsAdminAsync(context, userContext.UserId, cancellationToken))
        {
            return Result.Failure<ClientAccountStatusResponse>(ClientAccounts.Forbidden);
        }

        User? user = await context.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<ClientAccountStatusResponse>(UserErrors.NotFound(command.UserId));
        }

        if (!user.IsDeleted)
        {
            return new ClientAccountStatusResponse(user.Id, null, null);
        }

        user.DeletedAtUtc = null;
        user.DeletedByUserId = null;
        user.DeletionReason = null;

        await ClientAccounts.LogOnPfaAsync(
            context,
            user.Id,
            userContext.UserId,
            "AccountReopened",
            "Cont redeschis. Abonamentul se alege din nou.",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return new ClientAccountStatusResponse(user.Id, null, null);
    }
}
