using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Domain.Companies;
using Domain.Payments;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Companies.Onboarding;

public sealed record FleetBcrRequest(Guid UserId, string Email, string? Company, string? Cui, DateTime? ConfirmedAtUtc);

public sealed class FleetBcrService(IApplicationDbContext context, IStripeService stripe)
{
    public async Task<List<FleetBcrRequest>> ListAsync(CancellationToken ct)
    {
        // JSON state uses a value converter; filter its fields after materialization.
        List<User> users = await context.Users.AsNoTracking().Where(u => u.Role == UserRole.CarPoster).ToListAsync(ct);
        return users.Where(u => u.FleetOnboarding.BcrRequested)
            .Select(u => new FleetBcrRequest(u.Id, u.Email, u.FleetOnboarding.Company?.Name,
                u.FleetOnboarding.Company?.Cui, u.FleetOnboarding.BcrEligibleAtUtc)).ToList();
    }

    public async Task<Result> ConfirmAsync(Guid userId, CancellationToken ct)
    {
        User? user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.CarPoster, ct);
        if (user is null || !user.FleetOnboarding.BcrRequested)
        {
            return Result.Failure(Error.NotFound("Fleet.BcrMissing", "Solicitarea BCR nu a fost găsită."));
        }
        if (user.FleetOnboarding.BcrEligibleAtUtc is not null)
        {
            return Result.Success();
        }

        if (user.FleetOnboarding.CheckoutAttemptId is not null)
        {
            return Result.Failure(Error.Conflict("Fleet.CheckoutOpen", "Există o plată în curs. Confirmă după închiderea sesiunii, pentru a păstra suma afișată."));
        }

        UserSubscription? subscription = await context.UserSubscriptions.SingleOrDefaultAsync(s => s.UserId == userId && s.Plan == SubscriptionPlan.Fleet, ct);
        if (subscription?.Status == SubscriptionStatus.Active && subscription.StripeSubscriptionId is not null)
        {
            string coupon = await stripe.EnsureAdvanceCreditCouponAsync(FleetPricing.BcrCoupon(subscription.BillingCycle), ct);
            await stripe.ApplySubscriptionCouponAsync(subscription.StripeSubscriptionId, coupon, ct);
            subscription.BcrDiscountConfirmedAtUtc = DateTime.UtcNow;
        }
        user.FleetOnboarding.BcrEligibleAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);
        return Result.Success();
    }
}
