using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Payments.GrantDashboardAccess;

internal sealed class GrantDashboardAccessCommandHandler(IApplicationDbContext context)
    : ICommandHandler<GrantDashboardAccessCommand>
{
    public async Task<Result> Handle(GrantDashboardAccessCommand command, CancellationToken cancellationToken)
    {
        List<UserSubscription> subscriptionsToUpdate = await context.UserSubscriptions
            .Where(s => !s.DashboardAccessGranted && 
                        (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.ActivePendingBilling || s.Status == SubscriptionStatus.PaidPendingAccess))
            .ToListAsync(cancellationToken);

        if (subscriptionsToUpdate.Count == 0)
        {
            return Result.Success();
        }

        foreach (UserSubscription sub in subscriptionsToUpdate)
        {
            sub.DashboardAccessGranted = true;
            sub.DashboardAccessGrantedUtc = DateTime.UtcNow;
            
            if (sub.Status == SubscriptionStatus.PaidPendingAccess)
            {
                sub.Status = SubscriptionStatus.ActivePendingBilling;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
