using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Documents;
using Domain.Payments;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Admin;

public sealed record GetAdminPfaDetailQuery(Guid PfaRegistrationId) : IQuery<AdminPfaDetailResponse>;

internal sealed class GetAdminPfaDetailQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetAdminPfaDetailQuery, AdminPfaDetailResponse>
{
    public async Task<Result<AdminPfaDetailResponse>> Handle(
        GetAdminPfaDetailQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .Include(p => p.User)
            .Include(p => p.Documents)
            .SingleOrDefaultAsync(p => p.Id == query.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<AdminPfaDetailResponse>(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        UserSubscription? subscription = await context.UserSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == pfa.UserId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        List<PaymentRecord> payments = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.UserId == pfa.UserId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        PfaMonthlyIncome? currentMonthIncome = await context.PfaMonthlyIncomes
            .AsNoTracking()
            .Where(i =>
                i.PfaRegistrationId == pfa.Id &&
                i.Year == DateTime.UtcNow.Year &&
                i.Month == DateTime.UtcNow.Month)
            .FirstOrDefaultAsync(cancellationToken);

        PfaMonthlyIncome? lastProcessedIncome = await context.PfaMonthlyIncomes
            .AsNoTracking()
            .Where(i => i.PfaRegistrationId == pfa.Id && i.IsProcessed)
            .OrderByDescending(i => i.Year)
            .ThenByDescending(i => i.Month)
            .FirstOrDefaultAsync(cancellationToken);

        string internalNote = await context.PfaInternalNotes
            .AsNoTracking()
            .Where(n => n.PfaRegistrationId == pfa.Id)
            .OrderByDescending(n => n.UpdatedAtUtc ?? n.CreatedAtUtc)
            .Select(n => n.Content)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        List<AdminPfaActivityLogRow> logs = await context.PfaActivityLogs
            .AsNoTracking()
            .Include(l => l.PerformedByUser)
            .Where(l => l.PfaRegistrationId == pfa.Id)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(20)
            .Select(l => new AdminPfaActivityLogRow(
                l.Id,
                l.Description,
                l.CreatedAtUtc,
                $"{l.PerformedByUser.FirstName} {l.PerformedByUser.LastName}"))
            .ToListAsync(cancellationToken);

        DateTime? lastActivityAtUtc = await context.ChatRooms
            .AsNoTracking()
            .Where(r => r.ClientUserId == pfa.UserId)
            .OrderByDescending(r => r.LastMessageAtUtc)
            .Select(r => (DateTime?)r.LastMessageAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        string? lastProcessedMonth = lastProcessedIncome is null
            ? null
            : $"{lastProcessedIncome.Month:00}/{lastProcessedIncome.Year}";

        long? priceBani = subscription is null
            ? null
            : AdminBillingLabels.WeeklyPriceBani(subscription.Plan);

        var response = new AdminPfaDetailResponse(
            pfa.Id,
            pfa.UserId,
            GetAdminOverviewQueryHandler.CompanyName(pfa),
            GetAdminOverviewQueryHandler.HolderName(pfa),
            pfa.User.Email,
            pfa.Phone ?? "Telefon necompletat",
            GetAdminOverviewQueryHandler.AccountStatus(pfa.Status, subscription?.Status),
            AdminBillingLabels.PlanLabel(subscription?.Plan),
            GetAdminOverviewQueryHandler.SubscriptionStatusLabel(subscription?.Status),
            pfa.RegistrationType.ToString(),
            GetAdminOverviewQueryHandler.CurrentMonthStatus(currentMonthIncome, pfa.Documents),
            GetAdminOverviewQueryHandler.RelativeTime(lastActivityAtUtc ?? pfa.CreatedAtUtc),
            priceBani,
            subscription?.CreatedAtUtc,
            subscription?.NextBillingDateUtc,
            payments.FirstOrDefault(p => p.Status == PaymentStatus.Succeeded)?.CreatedAtUtc,
            payments.Count(p => p.Status == PaymentStatus.Failed),
            null,
            GetAdminOverviewQueryHandler.CustomerAge(pfa.CreatedAtUtc),
            lastProcessedMonth,
            pfa.Documents.Count(d => d.Status == DocumentStatus.Rejected),
            pfa.Documents.Count(d => d.Status == DocumentStatus.Pending),
            internalNote,
            logs);

        return response;
    }
}
