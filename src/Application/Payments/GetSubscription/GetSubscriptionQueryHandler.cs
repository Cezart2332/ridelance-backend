using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Payments;
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
        UserSubscription? sub = await context.UserSubscriptions
            .Where(s => s.UserId == query.UserId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (sub is null)
        {
            return Result.Success<SubscriptionResponse?>(null);
        }

        // Auto Catch-Up for local development/testing where NextBillingDateUtc is in the past
        if ((sub.Status == SubscriptionStatus.Active || sub.Status == SubscriptionStatus.ActivePendingBilling) &&
            sub.NextBillingDateUtc.HasValue &&
            sub.NextBillingDateUtc.Value < DateTime.UtcNow)
        {
            DateTime currentBilling = sub.NextBillingDateUtc.Value;
            DateTime nowUtc = DateTime.UtcNow;
            bool updated = false;

            while (currentBilling < nowUtc)
            {
                currentBilling = currentBilling.AddDays(7);

                // If there's a pending plan change scheduled, it takes effect at this billing point
                if (sub.PendingPlan.HasValue)
                {
                    sub.Plan = sub.PendingPlan.Value;
                    sub.PendingPlan = null;
                }

                // Record the weekly subscription payment in the database (with the now-active plan)
                var record = new PaymentRecord
                {
                    Id = Guid.NewGuid(),
                    UserId = sub.UserId,
                    PaymentType = PaymentType.Subscription,
                    Status = PaymentStatus.Succeeded,
                    AmountBani = GetPlanPriceBani(sub.Plan),
                    Description = $"RIDElance {sub.Plan} — abonament săptămânal",
                    StripePaymentId = $"mock_pay_{Guid.NewGuid().ToString("N")[..8]}",
                    CreatedAtUtc = currentBilling.AddDays(-7), // The payment occurred at the start of that billing week
                };
                context.PaymentRecords.Add(record);
                updated = true;
            }

            if (updated)
            {
                sub.NextBillingDateUtc = currentBilling;
                sub.Status = SubscriptionStatus.Active;
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        Domain.PfaRegistrations.PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.UserId == query.UserId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        bool hasPaidInfiintare = await context.PaymentRecords
            .AnyAsync(r => r.UserId == query.UserId &&
                           r.PaymentType == PaymentType.OneTime &&
                           r.Status == PaymentStatus.Succeeded &&
                           (r.Description.Contains("pfa") ||
                            r.Description.Contains("înființare") ||
                            r.Description.Contains("infiintare") ||
                            r.Description.Contains("Serviciu") ||
                            r.AmountBani == 45000 ||
                            r.AmountBani == 79900),
                      cancellationToken);


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
            pfa?.RegistrationType.ToString(),
            sub.PendingPlan?.ToString(),
            hasPaidInfiintare));
    }


    private static long GetPlanPriceBani(SubscriptionPlan plan)
    {
        return plan switch
        {
            SubscriptionPlan.Solo => 4900,
            SubscriptionPlan.Start => 9900,
            SubscriptionPlan.Pro => 14900,
            _ => 9900
        };
    }
}
