using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Companies;
using Domain.Payments;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Admin.Srl;

/// <summary>Un cont de firmă (SRL), cum îl vede adminul în lista „SRL înrolate”.</summary>
public sealed record AdminSrlAccountRow(
    Guid UserId,
    string CompanyName,
    string? Cui,
    string ContactName,
    string Email,
    string? Phone,
    string Plan,
    string SubscriptionStatus,
    string? BillingCycle,
    DateTime? NextBillingDateUtc,
    /// <summary>Activ = abonament plătit; inactiv = înrolat, dar fără abonament activ.</summary>
    bool SubscriptionActive,
    /// <summary>Onboardingul firmei e terminat (sau contul e dinainte să existe onboardingul).</summary>
    bool Enrolled,
    int OnboardingStep,
    int CarsTotal,
    int CarsPublished,
    /// <summary>Anunțuri plătite separat, peste cele incluse în abonament.</summary>
    int PaidExtraListings,
    int IncludedListings,
    string? CompanySlug,
    DateTime CreatedAtUtc,
    DateTime? LastActivityAtUtc,
    DateTime? DeletedAtUtc,
    string? DeletionReason);

public sealed record GetAdminSrlAccountsQuery : IQuery<IReadOnlyList<AdminSrlAccountRow>>;

internal sealed class GetAdminSrlAccountsQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetAdminSrlAccountsQuery, IReadOnlyList<AdminSrlAccountRow>>
{
    public async Task<Result<IReadOnlyList<AdminSrlAccountRow>>> Handle(
        GetAdminSrlAccountsQuery query,
        CancellationToken cancellationToken)
    {
        // Onboardingul firmei stă într-o coloană jsonb, deci se citește în memorie; conturile de
        // firmă sunt de ordinul sutelor, nu al milioanelor.
        List<User> users = await context.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.CarPoster)
            .ToListAsync(cancellationToken);

        Guid[] ids = users.Select(u => u.Id).ToArray();

        Dictionary<Guid, CompanyProfile> profiles = await context.CompanyProfiles
            .AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        var subscriptions = (await context.UserSubscriptions
                .AsNoTracking()
                .Where(s => ids.Contains(s.UserId))
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CreatedAtUtc).First());

        var cars = await context.Cars
            .AsNoTracking()
            .Where(c => c.PostedByUserId != null && ids.Contains(c.PostedByUserId.Value))
            .Select(c => new { OwnerId = c.PostedByUserId!.Value, c.ListingStatus, c.PaymentStatus })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, DateTime?> chatActivity = await context.ChatRooms
            .AsNoTracking()
            .Where(r => ids.Contains(r.ClientUserId))
            .GroupBy(r => r.ClientUserId)
            .Select(g => new { UserId = g.Key, Last = (DateTime?)g.Max(r => r.LastMessageAtUtc) })
            .ToDictionaryAsync(x => x.UserId, x => x.Last, cancellationToken);

        var rows = users
            .Select(user =>
            {
                CompanyProfile? profile = profiles.GetValueOrDefault(user.Id);
                UserSubscription? subscription = subscriptions.GetValueOrDefault(user.Id);
                var own = cars.Where(c => c.OwnerId == user.Id).ToList();
                FleetOnboarding onboarding = user.FleetOnboarding;

                string companyName =
                    NonEmpty(profile?.LegalName)
                    ?? NonEmpty(onboarding.Company?.Name)
                    ?? UserDisplayName.Of(user);

                return new AdminSrlAccountRow(
                    user.Id,
                    companyName,
                    NonEmpty(profile?.Cui) ?? NonEmpty(onboarding.Company?.Cui),
                    UserDisplayName.Of(user),
                    user.Email,
                    NonEmpty(profile?.Phone) ?? user.PhoneNumber,
                    AdminBillingLabels.PlanLabel(subscription?.Plan),
                    GetAdminOverviewQueryHandler.SubscriptionStatusLabel(subscription?.Status),
                    CycleLabel(subscription),
                    subscription?.NextBillingDateUtc,
                    subscription is not null && GetAdminOverviewQueryHandler.IsActiveSubscription(subscription.Status),
                    SrlEnrollment.IsEnrolled(user),
                    onboarding.CompletedStep,
                    own.Count,
                    own.Count(c => c.ListingStatus == ListingStatus.Published),
                    own.Count(c => c.PaymentStatus == CarListingPaymentStatus.Paid),
                    ListingAllowance.IncludedInFleetPlan,
                    NonEmpty(profile?.Slug),
                    user.CreatedAtUtc,
                    GetAdminOverviewQueryHandler.LatestActivity(user.LastActivityAtUtc, chatActivity.GetValueOrDefault(user.Id)),
                    user.DeletedAtUtc,
                    user.DeletionReason);
            })
            .OrderByDescending(r => r.LastActivityAtUtc ?? r.CreatedAtUtc)
            .ToList();

        return rows;
    }

    private static string? CycleLabel(UserSubscription? subscription)
    {
        if (subscription is null)
        {
            return null;
        }

        return subscription.BillingCycle == SubscriptionBillingCycle.Annual ? "Anual" : "Lunar";
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Când e o firmă „înrolată”: aceeași regulă în listă și în privirea de ansamblu.</summary>
internal static class SrlEnrollment
{
    /// <summary>
    /// Onboardingul firmei terminat. Conturile create înainte să existe onboardingul
    /// (<see cref="User.FleetOnboardingRequired" /> fals) sunt înrolate de la sine.
    /// </summary>
    public static bool IsEnrolled(User user) =>
        !user.FleetOnboardingRequired || user.FleetOnboarding.CompletedAtUtc is not null;
}
