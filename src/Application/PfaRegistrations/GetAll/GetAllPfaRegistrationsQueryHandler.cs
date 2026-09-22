using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Admin;
using Application.FiscalProfiles;
using Domain.FiscalProfiles;
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
    /// <summary>
    /// Un rând din listă, înainte de abonament. Același pentru un dosar și pentru un cont fără
    /// dosar, ca ordonarea și paginarea să se facă o singură dată, peste amândouă.
    /// </summary>
    private sealed record Row(
        Guid Id,
        Guid UserId,
        string UserEmail,
        string UserFirstName,
        string UserLastName,
        RegistrationType? RegistrationType,
        PfaRegistrationStatus? Status,
        DateTime? OnboardingCompletedAtUtc,
        string? FullName,
        string? Phone,
        int? ContractDuration,
        string? Street,
        string? Number,
        string? City,
        string? County,
        bool IsOwner,
        string? Cui,
        int DocumentCount,
        bool AwaitingAdminAction,
        DateTime CreatedAtUtc,
        DateTime? UserLastActivityAtUtc,
        DateTime? ChatActivityAtUtc,
        DateTime? DeletedAtUtc,
        PfaTaxProfileStatus? FiscalProfileStatus = null);

    public async Task<Result<PfaRegistrationListResponse>> Handle(
        GetAllPfaRegistrationsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<PfaRegistration> queryable = context.PfaRegistrations.AsQueryable();

        // If the caller is a Contabil, only show their assigned PFAs
        User? caller = await context.Users
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        bool isContabil = caller?.Role == UserRole.Contabil;
        if (isContabil)
        {
            // Contabilul lucrează doar cu clienți activi; un cont închis rămâne vizibil adminului.
            queryable = queryable.Where(r => r.AssignedContabilId == userContext.UserId && r.User.DeletedAtUtc == null);
        }

        int taxYear = DateTime.UtcNow.Year;

        List<Row> rows = await queryable
            .AsNoTracking()
            .Select(r => new Row(
                r.Id,
                r.UserId,
                r.User.Email,
                r.User.FirstName,
                r.User.LastName,
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
                r.Documents.Count,
                // „Mingea e la noi”: dosar PFA nereviewuit, o secțiune trimisă la validare, sau
                // pasul fiscal trimis spre alocarea pachetului de semnături (RL-02). Calculat în
                // SQL, ca filtrul rapid din admin să nu ceară încărcarea grafului per dosar.
                r.Status == PfaRegistrationStatus.Pending
                    || r.OnboardingSections.Any(s => s.Status == OnboardingSectionStatus.AwaitingValidation)
                    || r.SignaturePacket != null
                        && r.SignaturePacket.SubmittedForReviewAtUtc != null
                        && r.SignaturePacket.Status == SignaturePacketStatus.Draft,
                r.CreatedAtUtc,
                r.User.LastActivityAtUtc,
                context.ChatRooms
                    .Where(cr => cr.ClientUserId == r.UserId)
                    .OrderByDescending(cr => cr.LastMessageAtUtc)
                    .Select(cr => (DateTime?)cr.LastMessageAtUtc)
                    .FirstOrDefault(),
                r.User.DeletedAtUtc,
                context.PfaTaxProfiles
                    .Where(t => t.PfaRegistrationId == r.Id && t.TaxYear == taxYear)
                    .Select(t => (PfaTaxProfileStatus?)t.Status)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        // Conturile de client care n-au încă dosar PFA. Dosarul se naște abia la pasul 2, dar
        // clientul e în înrolare din clipa în care și-a făcut contul — iar pasul 1 (Eligibilitate)
        // îl validează adminul, deci trebuie să-l vadă înainte să existe dosarul. Fără ei, un
        // client cu actele încărcate nu apărea nicăieri, iar pasul 2 nu i se deschidea niciodată.
        //
        // Contabilul nu-i vede: el primește dosare alocate, iar ăștia n-au încă nimic de alocat.
        if (!isContabil)
        {
            List<Row> accountsWithoutRegistration = await context.Users
                .AsNoTracking()
                .Where(u => u.Role == UserRole.Client && !context.PfaRegistrations.Any(r => r.UserId == u.Id))
                .Select(u => new Row(
                    // Fără dosar, rândul se adresează prin contul clientului.
                    u.Id,
                    u.Id,
                    u.Email,
                    u.FirstName,
                    u.LastName,
                    null,
                    null,
                    null,
                    null,
                    u.PhoneNumber,
                    null,
                    null,
                    null,
                    null,
                    null,
                    false,
                    null,
                    context.Documents.Count(d => d.UserId == u.Id),
                    // Eligibilitatea așteaptă verdictul: acte încărcate, nevalidate, și nici
                    // respinse după ultima încărcare.
                    context.Documents.Any(d => d.UserId == u.Id)
                        && !context.OnboardingEligibilityProfiles.Any(p =>
                            p.UserId == u.Id
                            && (p.AdminValidatedAtUtc != null
                                || p.AdminRejectedAtUtc != null
                                    && !context.Documents.Any(d => d.UserId == u.Id && d.UploadedAtUtc > p.AdminRejectedAtUtc))),
                    u.CreatedAtUtc,
                    u.LastActivityAtUtc,
                    context.ChatRooms
                        .Where(cr => cr.ClientUserId == u.Id)
                        .OrderByDescending(cr => cr.LastMessageAtUtc)
                        .Select(cr => (DateTime?)cr.LastMessageAtUtc)
                        .FirstOrDefault(),
                    u.DeletedAtUtc))
                .ToListAsync(cancellationToken);

            rows.AddRange(accountsWithoutRegistration);
        }

        int totalCount = rows.Count;

        // ponytail: sort in memory because activity is max(login, chat); move to SQL if PFA count grows.
        var pagedData = rows
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
                bool hasRegistration = x.Status is not null;
                SubscriptionStatus? latestStatus = hasSubscription ? subscription!.Status : null;
                string accountStatus = AccountStatusOf(x, latestStatus);

                return new PfaRegistrationSummary(
                    x.Id,
                    x.UserId,
                    x.UserEmail,
                    UserDisplayName.Of(x.UserFirstName, x.UserLastName, x.UserEmail),
                    x.RegistrationType?.ToString() ?? string.Empty,
                    x.Status?.ToString() ?? WithoutRegistrationStatus,
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
                    GetAdminOverviewQueryHandler.LatestActivity(x.UserLastActivityAtUtc, x.ChatActivityAtUtc),
                    x.OnboardingCompletedAtUtc,
                    hasRegistration,
                    x.DeletedAtUtc,
                    FiscalProfileService.StatusCode(x.FiscalProfileStatus ?? PfaTaxProfileStatus.NotStarted));
            })
            .ToList();

        return new PfaRegistrationListResponse(items, totalCount);
    }

    /// <summary>Statusul rândurilor fără dosar: nici „Pending” (n-are ce aproba), nici altceva real.</summary>
    internal const string WithoutRegistrationStatus = "NoRegistration";

    private static string AccountStatusOf(Row row, SubscriptionStatus? latestStatus)
    {
        // Un cont închis rămâne în listă, cu istoricul lui — dar statusul spune că e închis.
        if (row.DeletedAtUtc is not null)
        {
            return "Închis";
        }

        if (row.Status is not PfaRegistrationStatus status)
        {
            return "Cont nou";
        }

        return ResolveAccountStatus(status, row.OnboardingCompletedAtUtc, latestStatus);
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
