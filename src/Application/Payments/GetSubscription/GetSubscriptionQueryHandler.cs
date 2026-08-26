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
        Domain.PfaRegistrations.PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.UserId == query.UserId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        bool hasPaidInfiintare = await InfiintarePaymentCheck.HasPaidAsync(
            context, query.UserId, cancellationToken);

        // Onboarding complet = înrolat = toate secțiunile obligatorii validate.
        // Semnalul unic este OnboardingCompletedAtUtc (setat de OnboardingProgress),
        // nu mai deducem din Status==Approved (care înseamnă doar „dosar PFA aprobat").
        bool onboardingSectionsValidated = pfa?.OnboardingCompletedAtUtc is not null;

        UserSubscription? sub = await context.UserSubscriptions
            .Where(s => s.UserId == query.UserId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (sub is null)
        {
            return Result.Success<SubscriptionResponse?>(new SubscriptionResponse(
                null,
                null,
                "NoSubscription",
                null,
                null,
                null,
                null,
                false,
                null,
                pfa?.Status.ToString(),
                pfa?.RegistrationType.ToString(),
                null,
                hasPaidInfiintare,
                onboardingSectionsValidated));
        }

        // Aici stătea un „auto catch-up": la fiecare citire a abonamentului, dacă data următoarei
        // facturări era în trecut, se inventau plăți săptămânale (`mock_pay_...`) cu sume scrise
        // de mână, direct în istoricul real al clientului. Cu facturarea lunară prin Stripe,
        // singura sursă de plăți e `invoice.payment_succeeded` din webhook — o citire nu are voie
        // să scrie o încasare care nu s-a întâmplat.

        return Result.Success<SubscriptionResponse?>(new SubscriptionResponse(
            sub.Id,
            sub.Plan.ToString(),
            sub.Status.ToString(),
            sub.StripeSubscriptionId,
            sub.FirstBillingDateUtc,
            sub.NextBillingDateUtc,
            sub.CreatedAtUtc,
            sub.DashboardAccessGranted,
            sub.BillingCycle.ToString(),
            pfa?.Status.ToString(),
            pfa?.RegistrationType.ToString(),
            sub.PendingPlan?.ToString(),
            hasPaidInfiintare,
            onboardingSectionsValidated));
    }

}
