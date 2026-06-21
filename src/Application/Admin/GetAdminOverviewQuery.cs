using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Documents;
using Domain.Payments;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Admin;

public sealed record GetAdminOverviewQuery(AdminOverviewFilters Filters) : IQuery<AdminOverviewResponse>;

internal sealed class GetAdminOverviewQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetAdminOverviewQuery, AdminOverviewResponse>
{
    private const long CarListingMonthlyPriceBani = 3000;

    public async Task<Result<AdminOverviewResponse>> Handle(
        GetAdminOverviewQuery query,
        CancellationToken cancellationToken)
    {
        (DateTime startUtc, DateTime endUtc) = ResolveDateRange(query.Filters);

        List<PaymentRecord> payments = await context.PaymentRecords
            .AsNoTracking()
            .Include(p => p.User)
            .Where(p => p.CreatedAtUtc >= startUtc && p.CreatedAtUtc <= endUtc)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        List<ServiceOrder> serviceOrders = await context.ServiceOrders
            .AsNoTracking()
            .Where(o => (o.PaidAtUtc ?? o.CreatedAtUtc) >= startUtc && (o.PaidAtUtc ?? o.CreatedAtUtc) <= endUtc)
            .OrderByDescending(o => o.PaidAtUtc ?? o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        List<Car> cars = await context.Cars
            .AsNoTracking()
            .Include(c => c.Leads)
            .ToListAsync(cancellationToken);

        List<CarLead> leads = await context.CarLeads
            .AsNoTracking()
            .Where(l => l.CreatedAtUtc >= startUtc && l.CreatedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

        List<PfaRegistration> pfas = await context.PfaRegistrations
            .AsNoTracking()
            .Include(p => p.User)
            .Include(p => p.Documents)
            .ToListAsync(cancellationToken);

        ApplyPreSubscriptionFilters(query.Filters, payments, serviceOrders, cars, leads, pfas);

        Guid[] userIds = pfas.Select(p => p.UserId).Distinct().ToArray();
        List<UserSubscription> subscriptions = await context.UserSubscriptions
            .AsNoTracking()
            .Where(s => userIds.Contains(s.UserId))
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var latestSubscriptions = subscriptions
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CreatedAtUtc).First());

        ApplyPlanFilter(query.Filters, subscriptions, latestSubscriptions, pfas);

        Dictionary<Guid, DateTime?> lastActivityByUserId = await context.ChatRooms
            .AsNoTracking()
            .Where(r => userIds.Contains(r.ClientUserId))
            .GroupBy(r => r.ClientUserId)
            .Select(g => new { UserId = g.Key, LastActivityAtUtc = (DateTime?)g.Max(r => r.LastMessageAtUtc) })
            .ToDictionaryAsync(x => x.UserId, x => x.LastActivityAtUtc, cancellationToken);

        List<PfaMonthlyIncome> currentMonthIncomes = await context.PfaMonthlyIncomes
            .AsNoTracking()
            .Where(i => i.Year == DateTime.UtcNow.Year && i.Month == DateTime.UtcNow.Month)
            .ToListAsync(cancellationToken);

        long succeededPaymentRevenue = payments
            .Where(p => p.Status == PaymentStatus.Succeeded)
            .Sum(p => p.AmountBani);

        long paidServiceRevenue = serviceOrders
            .Where(o => o.Status == ServiceOrderStatus.Paid)
            .Sum(o => o.AmountBani ?? 0);

        int paidActiveCars = cars.Count(c =>
            c.Active &&
            c.ApprovalStatus == CarApprovalStatus.Approved &&
            c.PaymentStatus == CarListingPaymentStatus.Paid);

        long carMonthlyRevenue = paidActiveCars * CarListingMonthlyPriceBani;

        long pfaMonthlyRecurringRevenue = latestSubscriptions.Values
            .Where(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.ActivePendingBilling)
            .Sum(s => AdminBillingLabels.MonthlyEstimateBani(s.Plan));

        var recentPayments = BuildPaymentRows(payments, serviceOrders, cars)
            .OrderByDescending(p => p.DateUtc)
            .Take(8)
            .ToList();

        var failedPayments = BuildPaymentRows(
                payments.Where(p => p.Status == PaymentStatus.Failed).ToList(),
                serviceOrders.Where(o => o.Status != ServiceOrderStatus.Paid).ToList(),
                cars.Where(c => c.PaymentStatus == CarListingPaymentStatus.PastDue).ToList())
            .OrderByDescending(p => p.DateUtc)
            .Take(8)
            .ToList();

        var enrolledPfas = pfas
            .Where(p => p.Status == PfaRegistrationStatus.Approved)
            .OrderByDescending(p => lastActivityByUserId.GetValueOrDefault(p.UserId) ?? p.CreatedAtUtc)
            .Take(12)
            .Select(p => ToPfaCard(
                p,
                latestSubscriptions.GetValueOrDefault(p.UserId),
                currentMonthIncomes.FirstOrDefault(i => i.PfaRegistrationId == p.Id),
                lastActivityByUserId.GetValueOrDefault(p.UserId)))
            .ToList();

        var response = new AdminOverviewResponse(
            IsFallback: false,
            GeneratedAtUtc: DateTime.UtcNow,
            FinancialKpis: new AdminFinancialKpis(
                TotalCurrentMonthRevenueBani: succeededPaymentRevenue + paidServiceRevenue + carMonthlyRevenue,
                EstimatedMonthlyRecurringRevenueBani: pfaMonthlyRecurringRevenue + carMonthlyRevenue,
                OneTimeCurrentMonthRevenueBani: payments.Where(p => p.PaymentType == PaymentType.OneTime && p.Status == PaymentStatus.Succeeded).Sum(p => p.AmountBani) + paidServiceRevenue,
                PartnerCommissionsBani: 0,
                SuccessfulPayments: payments.Count(p => p.Status == PaymentStatus.Succeeded) + serviceOrders.Count(o => o.Status == ServiceOrderStatus.Paid),
                FailedPayments: payments.Count(p => p.Status == PaymentStatus.Failed) + cars.Count(c => c.PaymentStatus == CarListingPaymentStatus.PastDue)),
            RevenueCategories:
            [
                new("Abonamente PFA", pfaMonthlyRecurringRevenue, latestSubscriptions.Count),
                new("Anunțuri auto lunare", carMonthlyRevenue, paidActiveCars),
                new("Servicii individuale", paidServiceRevenue, serviceOrders.Count(o => o.Status == ServiceOrderStatus.Paid)),
                new("Comisioane parteneri", 0),
                new("Alte venituri", payments.Where(p => p.Status == PaymentStatus.Succeeded && p.PaymentType != PaymentType.Subscription && !p.Description.Contains("PFA", StringComparison.OrdinalIgnoreCase)).Sum(p => p.AmountBani))
            ],
            PfaSubscriptions: BuildPfaSubscriptionMetrics(latestSubscriptions.Values),
            CarSubscriptions:
            [
                new("Anunțuri auto active", cars.Count(c => c.Active)),
                new("Anunțuri în verificare", cars.Count(c => c.ApprovalStatus == CarApprovalStatus.Pending)),
                new("Anunțuri cu plată activă", paidActiveCars),
                new("Anunțuri cu plată eșuată", cars.Count(c => c.PaymentStatus == CarListingPaymentStatus.PastDue)),
                new("Anunțuri suspendate", cars.Count(c => !c.Active))
            ],
            RecentPayments: recentPayments,
            FailedPayments: failedPayments,
            ServiceSales: serviceOrders.Take(8).Select(ToServiceSaleRow).ToList(),
            CarStats: new AdminCarStats(
                TotalListed: cars.Count,
                PaidActive: paidActiveCars,
                PendingReview: cars.Count(c => c.ApprovalStatus == CarApprovalStatus.Pending),
                FailedPayment: cars.Count(c => c.PaymentStatus == CarListingPaymentStatus.PastDue),
                LeadsGenerated: leads.Count,
                MonthlyRevenueBani: carMonthlyRevenue),
            PfaStats: new AdminPfaStats(
                TotalEnrolled: pfas.Count(p => p.Status == PfaRegistrationStatus.Approved),
                Active: pfas.Count(p => p.Status == PfaRegistrationStatus.Approved && latestSubscriptions.GetValueOrDefault(p.UserId)?.Status is SubscriptionStatus.Active or SubscriptionStatus.ActivePendingBilling),
                NewRequests: pfas.Count(p => p.Status == PfaRegistrationStatus.Pending),
                ClientBlocked: currentMonthIncomes.Count(i => !i.IsProcessed),
                Inactive: pfas.Count(p => p.Status == PfaRegistrationStatus.Approved && latestSubscriptions.GetValueOrDefault(p.UserId) is null),
                FailedPayment: latestSubscriptions.Values.Count(s => s.Status == SubscriptionStatus.PastDue)),
            EnrolledPfas: enrolledPfas);

        return response;
    }

    private static List<AdminMetric> BuildPfaSubscriptionMetrics(IEnumerable<UserSubscription> subscriptions)
    {
        var list = subscriptions.ToList();

        return
        [
            new("Solo active", list.Count(s => s.Plan == SubscriptionPlan.Solo && IsActiveSubscription(s.Status))),
            new("Start active", list.Count(s => s.Plan == SubscriptionPlan.Start && IsActiveSubscription(s.Status))),
            new("Pro active", list.Count(s => s.Plan == SubscriptionPlan.Pro && IsActiveSubscription(s.Status))),
            new("Trial", 0),
            new("Anulate", list.Count(s => s.Status == SubscriptionStatus.Cancelled)),
            new("Suspendate", list.Count(s => s.Status == SubscriptionStatus.Expired)),
            new("Plată eșuată", list.Count(s => s.Status == SubscriptionStatus.PastDue))
        ];
    }

    private static IEnumerable<AdminPaymentRow> BuildPaymentRows(
        IReadOnlyCollection<PaymentRecord> payments,
        IReadOnlyCollection<ServiceOrder> serviceOrders,
        IReadOnlyCollection<Car> cars)
    {
        foreach (PaymentRecord payment in payments)
        {
            yield return new AdminPaymentRow(
                payment.Id.ToString(),
                $"{payment.User.FirstName} {payment.User.LastName}".Trim(),
                payment.Description,
                payment.PaymentType == PaymentType.Subscription ? "Recurentă" : "One-time",
                payment.AmountBani,
                PaymentStatusLabel(payment.Status),
                payment.CreatedAtUtc,
                "Card / Stripe");
        }

        foreach (ServiceOrder order in serviceOrders)
        {
            yield return new AdminPaymentRow(
                order.Id.ToString(),
                string.IsNullOrWhiteSpace(order.CustomerName) ? order.CustomerEmail : order.CustomerName,
                order.ServiceTitle,
                "One-time",
                order.AmountBani ?? 0,
                order.Status == ServiceOrderStatus.Paid ? "Reușită" : "În așteptare",
                order.PaidAtUtc ?? order.CreatedAtUtc,
                "Card / Stripe");
        }

        foreach (Car car in cars)
        {
            if (car.PaymentStatus != CarListingPaymentStatus.PastDue)
            {
                continue;
            }

            yield return new AdminPaymentRow(
                $"car-{car.Id}",
                "Poster auto",
                $"{car.Brand} {car.Model} — anunț auto lunar",
                "Recurentă",
                CarListingMonthlyPriceBani,
                "Eșuată",
                car.PaidAtUtc ?? car.CreatedAtUtc,
                "Card / Stripe");
        }
    }

    private static AdminServiceSaleRow ToServiceSaleRow(ServiceOrder order) =>
        new(
            order.Id,
            string.IsNullOrWhiteSpace(order.CustomerName) ? order.CustomerEmail : order.CustomerName,
            order.ServiceTitle,
            order.AmountBani ?? 0,
            order.Status == ServiceOrderStatus.Paid ? "Reușită" : "În așteptare",
            order.Status == ServiceOrderStatus.Paid ? "De livrat" : "Așteaptă plata",
            "Admin",
            order.CreatedAtUtc);

    internal static AdminOverviewPfaCard ToPfaCard(
        PfaRegistration pfa,
        UserSubscription? subscription,
        PfaMonthlyIncome? currentMonthIncome,
        DateTime? lastActivityAtUtc) =>
        new(
            pfa.Id,
            pfa.UserId,
            CompanyName(pfa),
            HolderName(pfa),
            pfa.User.Email,
            pfa.Phone ?? "Telefon necompletat",
            AdminBillingLabels.PlanLabel(subscription?.Plan),
            SubscriptionStatusLabel(subscription?.Status),
            CustomerAge(pfa.CreatedAtUtc),
            AccountStatus(pfa.Status, subscription?.Status),
            CurrentMonthStatus(currentMonthIncome, pfa.Documents),
            RelativeTime(lastActivityAtUtc ?? pfa.CreatedAtUtc),
            lastActivityAtUtc);

    internal static string CompanyName(PfaRegistration pfa) =>
        !string.IsNullOrWhiteSpace(pfa.FullName)
            ? pfa.FullName
            : $"{pfa.User.FirstName} {pfa.User.LastName}".Trim();

    internal static string HolderName(PfaRegistration pfa) =>
        $"{pfa.User.FirstName} {pfa.User.LastName}".Trim();

    internal static string AccountStatus(PfaRegistrationStatus pfaStatus, SubscriptionStatus? subscriptionStatus)
    {
        if (pfaStatus == PfaRegistrationStatus.Pending)
        {
            return "În onboarding";
        }

        if (pfaStatus == PfaRegistrationStatus.Rejected)
        {
            return "Inactiv";
        }

        return subscriptionStatus switch
        {
            SubscriptionStatus.Active or SubscriptionStatus.ActivePendingBilling => "Activ",
            SubscriptionStatus.PastDue => "Activ",
            SubscriptionStatus.Expired => "Suspendat",
            SubscriptionStatus.Cancelled => "Inactiv",
            _ => "Gata de activare"
        };
    }

    internal static string CurrentMonthStatus(PfaMonthlyIncome? income, IReadOnlyCollection<Document> documents)
    {
        if (income?.IsProcessed == true)
        {
            return "Procesat";
        }

        if (documents.Any(d => d.Status == DocumentStatus.Pending))
        {
            return "În verificare";
        }

        return "Documente lipsă";
    }

    internal static string SubscriptionStatusLabel(SubscriptionStatus? status) => status switch
    {
        SubscriptionStatus.Active or SubscriptionStatus.ActivePendingBilling => "Activ",
        SubscriptionStatus.PaidPendingAccess => "Trial",
        SubscriptionStatus.PastDue => "Plată eșuată",
        SubscriptionStatus.Cancelled => "Anulat",
        SubscriptionStatus.Expired => "Suspendat",
        _ => "Fără abonament"
    };

    internal static string PaymentStatusLabel(PaymentStatus status) => status switch
    {
        PaymentStatus.Succeeded => "Reușită",
        PaymentStatus.Failed => "Eșuată",
        PaymentStatus.Pending => "În așteptare",
        PaymentStatus.Refunded => "Refund",
        _ => status.ToString()
    };

    internal static bool IsActiveSubscription(SubscriptionStatus status) =>
        status is SubscriptionStatus.Active or SubscriptionStatus.ActivePendingBilling;

    internal static string CustomerAge(DateTime createdAtUtc)
    {
        int weeks = Math.Max(0, (int)Math.Floor((DateTime.UtcNow - createdAtUtc).TotalDays / 7));
        return weeks switch
        {
            0 => "Client RIDElance activ de sub o săptămână",
            1 => "Client RIDElance activ de 1 săptămână",
            _ => $"Client RIDElance activ de {weeks} săptămâni"
        };
    }

    internal static string RelativeTime(DateTime utc)
    {
        TimeSpan diff = DateTime.UtcNow - utc;
        if (diff.TotalMinutes < 60)
        {
            return $"Acum {Math.Max(0, (int)diff.TotalMinutes)} minute";
        }

        if (diff.TotalHours < 24)
        {
            int hours = (int)diff.TotalHours;
            return $"Acum {hours} {(hours == 1 ? "oră" : "ore")}";
        }

        int days = (int)diff.TotalDays;
        return days == 1 ? "Ieri" : $"Acum {days} zile";
    }

    private static (DateTime StartUtc, DateTime EndUtc) ResolveDateRange(AdminOverviewFilters filters)
    {
        DateTime now = DateTime.UtcNow;

        if (string.Equals(filters.PeriodPreset, "custom", StringComparison.OrdinalIgnoreCase)
            && filters.DateFrom.HasValue
            && filters.DateTo.HasValue)
        {
            return (filters.DateFrom.Value.Date, filters.DateTo.Value.Date.AddDays(1).AddTicks(-1));
        }

        DateTime currentMonthStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return filters.PeriodPreset?.ToUpperInvariant() switch
        {
            "TODAY" => (now.Date, now.Date.AddDays(1).AddTicks(-1)),
            "7D" => (now.Date.AddDays(-6), now.Date.AddDays(1).AddTicks(-1)),
            "LAST_MONTH" => (currentMonthStart.AddMonths(-1), currentMonthStart.AddTicks(-1)),
            _ => (currentMonthStart, now.Date.AddDays(1).AddTicks(-1))
        };
    }

    private static void ApplyPreSubscriptionFilters(
        AdminOverviewFilters filters,
        List<PaymentRecord> payments,
        List<ServiceOrder> serviceOrders,
        List<Car> cars,
        List<CarLead> leads,
        List<PfaRegistration> pfas)
    {
        if (!string.IsNullOrWhiteSpace(filters.City))
        {
            string city = filters.City.Trim();
            pfas.RemoveAll(p => string.IsNullOrWhiteSpace(p.City) || !p.City.Contains(city, StringComparison.OrdinalIgnoreCase));
            cars.RemoveAll(c => string.IsNullOrWhiteSpace(c.Location) || !c.Location.Contains(city, StringComparison.OrdinalIgnoreCase));
            leads.RemoveAll(l => string.IsNullOrWhiteSpace(l.City) || !l.City.Contains(city, StringComparison.OrdinalIgnoreCase));
        }

        switch (filters.Product?.ToUpperInvariant())
        {
            case "PFA":
                cars.Clear();
                leads.Clear();
                serviceOrders.Clear();
                break;
            case "CARS":
                pfas.Clear();
                serviceOrders.Clear();
                payments.Clear();
                break;
            case "SERVICES":
                pfas.Clear();
                cars.Clear();
                leads.Clear();
                payments.RemoveAll(p => p.PaymentType != PaymentType.OneTime);
                break;
            case "PARTNERS":
                pfas.Clear();
                cars.Clear();
                leads.Clear();
                serviceOrders.Clear();
                payments.Clear();
                break;
        }

        switch (filters.RevenueType?.ToUpperInvariant())
        {
            case "RECURRING":
                serviceOrders.Clear();
                payments.RemoveAll(p => p.PaymentType != PaymentType.Subscription);
                break;
            case "ONE_TIME":
                cars.Clear();
                leads.Clear();
                payments.RemoveAll(p => p.PaymentType != PaymentType.OneTime);
                break;
            case "COMMISSION":
                pfas.Clear();
                cars.Clear();
                leads.Clear();
                serviceOrders.Clear();
                payments.Clear();
                break;
        }

        ApplyPaymentStatusFilter(filters, payments, serviceOrders, cars);
    }

    private static void ApplyPaymentStatusFilter(
        AdminOverviewFilters filters,
        List<PaymentRecord> payments,
        List<ServiceOrder> serviceOrders,
        List<Car> cars)
    {
        switch (filters.PaymentStatus?.ToUpperInvariant())
        {
            case "SUCCEEDED":
                payments.RemoveAll(p => p.Status != PaymentStatus.Succeeded);
                serviceOrders.RemoveAll(o => o.Status != ServiceOrderStatus.Paid);
                cars.RemoveAll(c => c.PaymentStatus != CarListingPaymentStatus.Paid);
                break;
            case "FAILED":
                payments.RemoveAll(p => p.Status != PaymentStatus.Failed);
                serviceOrders.Clear();
                cars.RemoveAll(c => c.PaymentStatus != CarListingPaymentStatus.PastDue);
                break;
            case "PENDING":
                payments.RemoveAll(p => p.Status != PaymentStatus.Pending);
                serviceOrders.RemoveAll(o => o.Status != ServiceOrderStatus.Pending);
                cars.RemoveAll(c => c.PaymentStatus != CarListingPaymentStatus.Pending);
                break;
            case "REFUND":
                payments.RemoveAll(p => p.Status != PaymentStatus.Refunded);
                serviceOrders.Clear();
                cars.Clear();
                break;
        }
    }

    private static void ApplyPlanFilter(
        AdminOverviewFilters filters,
        List<UserSubscription> subscriptions,
        Dictionary<Guid, UserSubscription> latestSubscriptions,
        List<PfaRegistration> pfas)
    {
        if (!Enum.TryParse(filters.Plan, ignoreCase: true, out SubscriptionPlan plan))
        {
            return;
        }

        subscriptions.RemoveAll(s => s.Plan != plan);

        var matchingUserIds = latestSubscriptions
            .Where(kvp => kvp.Value.Plan == plan)
            .Select(kvp => kvp.Key)
            .ToHashSet();

        pfas.RemoveAll(p => !matchingUserIds.Contains(p.UserId));

        foreach (Guid userId in latestSubscriptions.Keys.Where(userId => !matchingUserIds.Contains(userId)).ToList())
        {
            latestSubscriptions.Remove(userId);
        }
    }
}
