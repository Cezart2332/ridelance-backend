using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
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
                r.FullName,
                r.Phone,
                r.ContractDuration,
                r.Street,
                r.Number,
                r.City,
                r.County,
                r.IsOwner,
                DocumentCount = r.Documents.Count,
                r.CreatedAtUtc,
                LastActivityAtUtc = context.ChatRooms
                    .Where(cr => cr.ClientUserId == r.UserId)
                    .OrderByDescending(cr => cr.LastMessageAtUtc)
                    .Select(cr => (DateTime?)cr.LastMessageAtUtc)
                    .FirstOrDefault()
            })
            .OrderByDescending(x => x.LastActivityAtUtc ?? x.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        Guid[] userIds = pagedData.Select(x => x.UserId).Distinct().ToArray();
        Dictionary<Guid, SubscriptionStatus> latestSubscriptionStatuses = await context.UserSubscriptions
            .AsNoTracking()
            .Where(s => userIds.Contains(s.UserId))
            .GroupBy(s => s.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Status = g.OrderByDescending(s => s.CreatedAtUtc).Select(s => s.Status).First()
            })
            .ToDictionaryAsync(x => x.UserId, x => x.Status, cancellationToken);

        var items = pagedData
            .Select(x =>
            {
                latestSubscriptionStatuses.TryGetValue(x.UserId, out SubscriptionStatus subscriptionStatus);
                bool hasSubscription = latestSubscriptionStatuses.ContainsKey(x.UserId);
                string? subscriptionStatusText = hasSubscription ? subscriptionStatus.ToString() : null;
                string accountStatus = ResolveAccountStatus(x.Status, hasSubscription ? subscriptionStatus : null);

                return new PfaRegistrationSummary(
                    x.Id,
                    x.UserId,
                    x.UserEmail,
                    $"{x.UserFirstName} {x.UserLastName}",
                    x.RegistrationType.ToString(),
                    x.Status.ToString(),
                    accountStatus,
                    subscriptionStatusText,
                    x.FullName,
                    x.Phone,
                    x.ContractDuration,
                    x.Street,
                    x.Number,
                    x.City,
                    x.County,
                    x.IsOwner,
                    x.DocumentCount,
                    x.CreatedAtUtc,
                    x.LastActivityAtUtc);
            })
            .ToList();

        return new PfaRegistrationListResponse(items, totalCount);
    }

    private static string ResolveAccountStatus(PfaRegistrationStatus pfaStatus, SubscriptionStatus? subscriptionStatus)
    {
        if (pfaStatus != PfaRegistrationStatus.Approved)
        {
            return "Nou";
        }

        return subscriptionStatus is SubscriptionStatus.Active or SubscriptionStatus.ActivePendingBilling
            ? "Activ"
            : "Inactiv";
    }
}
