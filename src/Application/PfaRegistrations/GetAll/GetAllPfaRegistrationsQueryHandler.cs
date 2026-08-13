using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Admin;
using Domain.PfaRegistrations;
using Domain.Payments;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.GetAll;

internal sealed class GetAllPfaRegistrationsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetAllPfaRegistrationsQuery, PfaRegistrationListResponse>
{
    public async Task<Result<PfaRegistrationListResponse>> Handle(
        GetAllPfaRegistrationsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<PfaRegistration> queryable = context.PfaRegistrations.AsQueryable();

        // If the caller is a Contabil, only show their assigned PFAs
        User? caller = await context.Users
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller?.Role == UserRole.Contabil)
        {
            queryable = queryable.Where(r => r.AssignedContabilId == userContext.UserId);
        }

        int totalCount = await queryable.CountAsync(cancellationToken);

        var pagedData = await queryable
            .AsNoTracking()
            .Select(r => new
            {
                r.Id,
                r.UserId,
                UserEmail = r.User.Email,
                UserFirstName = r.User.FirstName,
                UserLastName = r.User.LastName,
                r.RegistrationType,
                r.Status,
                r.OnboardingCompletedAtUtc,
                r.FullName,
                r.Phone,
                r.ContractDuration,
                r.Street,
                r.Number,
                r.City,
                r.County,
                r.IsOwner,
                r.Cui,
                DocumentCount = r.Documents.Count,
                // „Mingea e la noi”: dosar PFA nereviewuit, o secțiune trimisă la validare, sau
                // pasul fiscal trimis spre alocarea pachetului de semnături (RL-02). Calculat în
                // SQL, ca filtrul rapid din admin să nu ceară încărcarea grafului per dosar.
                AwaitingAdminAction =
                    r.Status == PfaRegistrationStatus.Pending
                    || r.OnboardingSections.Any(s => s.Status == OnboardingSectionStatus.AwaitingValidation)
                    || r.SignaturePacket != null
                        && r.SignaturePacket.SubmittedForReviewAtUtc != null
                        && r.SignaturePacket.Status == SignaturePacketStatus.Draft,
                r.CreatedAtUtc,
                UserLastActivityAtUtc = r.User.LastActivityAtUtc,
                ChatActivityAtUtc = context.ChatRooms
                    .Where(cr => cr.ClientUserId == r.UserId)
                    .OrderByDescending(cr => cr.LastMessageAtUtc)
                    .Select(cr => (DateTime?)cr.LastMessageAtUtc)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // ponytail: sort in memory because activity is max(login, chat); move to SQL if PFA count grows.
        pagedData = pagedData
            .OrderByDescending(x => GetAdminOverviewQueryHandler.LatestActivity(x.UserLastActivityAtUtc, x.ChatActivityAtUtc) ?? x.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        Guid[] userIds = pagedData.Select(x => x.UserId).Distinct().ToArray();
        var latestSubscriptions = await context.UserSubscriptions
            .AsNoTracking()
            .Where(s => userIds.Contains(s.UserId))
            .GroupBy(s => s.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Subscription = g.OrderByDescending(s => s.CreatedAtUtc)
                    .Select(s => new { s.Status, s.Plan })
                    .First()
            })
            .ToDictionaryAsync(x => x.UserId, x => x.Subscription, cancellationToken);

        var items = pagedData
            .Select(x =>
            {
                bool hasSubscription = latestSubscriptions.TryGetValue(x.UserId, out var subscription);
                string? subscriptionStatusText = hasSubscription ? subscription!.Status.ToString() : null;
                string? subscriptionPlanText = hasSubscription ? subscription!.Plan.ToString() : null;
                string accountStatus = ResolveAccountStatus(x.Status, x.OnboardingCompletedAtUtc, hasSubscription ? subscription!.Status : null);

                return new PfaRegistrationSummary(
                    x.Id,
                    x.UserId,
                    x.UserEmail,
                    $"{x.UserFirstName} {x.UserLastName}",
                    x.RegistrationType.ToString(),
                    x.Status.ToString(),
                    accountStatus,
                    subscriptionStatusText,
                    subscriptionPlanText,
                    x.FullName,
                    x.Phone,
                    x.ContractDuration,
                    x.Street,
                    x.Number,
                    x.City,
                    x.County,
                    x.IsOwner,
                    x.Cui,
                    x.DocumentCount,
                    x.AwaitingAdminAction,
                    x.CreatedAtUtc,
                    GetAdminOverviewQueryHandler.LatestActivity(x.UserLastActivityAtUtc, x.ChatActivityAtUtc));
            })
            .ToList();

        return new PfaRegistrationListResponse(items, totalCount);
    }

    private static string ResolveAccountStatus(
        PfaRegistrationStatus pfaStatus,
        DateTime? onboardingCompletedAtUtc,
        SubscriptionStatus? subscriptionStatus)
    {
        if (pfaStatus != PfaRegistrationStatus.Approved)
        {
            return "Nou";
        }

        // Dosar PFA aprobat, dar onboardingul nu e complet → încă în onboarding, nu „înrolat".
        if (onboardingCompletedAtUtc is null)
        {
            return "În onboarding";
        }

        return subscriptionStatus is SubscriptionStatus.Active or SubscriptionStatus.ActivePendingBilling
            ? "Activ"
            : "Inactiv";
    }
}
