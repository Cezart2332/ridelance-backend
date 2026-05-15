using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Payments.GetSubscription;

internal sealed class GetSubscriptionQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetSubscriptionQuery, SubscriptionResponse?>
{
    public async Task<Result<SubscriptionResponse?>> Handle(
        GetSubscriptionQuery query,
        CancellationToken cancellationToken)
    {
        Domain.Payments.UserSubscription? sub = await context.UserSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == query.UserId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (sub is null)
        {
            return Result.Success<SubscriptionResponse?>(null);
        }

        Domain.PfaRegistrations.PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.UserId == query.UserId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success<SubscriptionResponse?>(new SubscriptionResponse(
            sub.Id,
            sub.Plan.ToString(),
            sub.Status.ToString(),
            sub.StripeSubscriptionId,
            sub.FirstBillingDateUtc,
            sub.NextBillingDateUtc,
            sub.CreatedAtUtc,
            sub.DashboardAccessGranted,
            pfa?.Status.ToString(),
            pfa?.RegistrationType.ToString()));
    }
}
