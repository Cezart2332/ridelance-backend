using Domain.Payments;

namespace Application.Admin;

public sealed record AdminOverviewFilters(
    string PeriodPreset,
    DateTime? DateFrom,
    DateTime? DateTo,
    string? RevenueType,
    string? Product,
    string? PaymentStatus,
    string? Plan,
    string? City,
    string? Partner);

public sealed record AdminOverviewResponse(
    bool IsFallback,
    DateTime GeneratedAtUtc,
    AdminFinancialKpis FinancialKpis,
    IReadOnlyList<AdminRevenueCategory> RevenueCategories,
    IReadOnlyList<AdminMetric> PfaSubscriptions,
    IReadOnlyList<AdminMetric> CarSubscriptions,
    IReadOnlyList<AdminPaymentRow> RecentPayments,
    IReadOnlyList<AdminPaymentRow> FailedPayments,
    IReadOnlyList<AdminServiceSaleRow> ServiceSales,
    AdminCarStats CarStats,
    AdminPfaStats PfaStats,
    IReadOnlyList<AdminOverviewPfaCard> EnrolledPfas,
    AdminSrlStats? SrlStats = null,
    IReadOnlyList<AdminMetric>? SrlSubscriptions = null);

public sealed record AdminFinancialKpis(
    long TotalCurrentMonthRevenueBani,
    long EstimatedMonthlyRecurringRevenueBani,
    long OneTimeCurrentMonthRevenueBani,
    long PartnerCommissionsBani,
    int SuccessfulPayments,
    int FailedPayments);

public sealed record AdminRevenueCategory(
    string Label,
    long AmountBani,
    int? Count = null);

public sealed record AdminMetric(
    string Label,
    int Value,
    long? AmountBani = null,
    string? Helper = null);

public sealed record AdminPaymentRow(
    string Id,
    string Client,
    string ProductOrService,
    string PaymentType,
    long AmountBani,
    string Status,
    DateTime DateUtc,
    string PaymentMethod);

public sealed record AdminServiceSaleRow(
    Guid Id,
    string Client,
    string Service,
    long PriceBani,
    string PaymentStatus,
    string DeliveryStatus,
    string Responsible,
    DateTime OrderedAtUtc);

public sealed record AdminCarStats(
    int TotalListed,
    int PaidActive,
    int PendingReview,
    int FailedPayment,
    int LeadsGenerated,
    long MonthlyRevenueBani);

public sealed record AdminPfaStats(
    int TotalEnrolled,
    int Active,
    int NewRequests,
    int ClientBlocked,
    int Inactive,
    int FailedPayment,
    // PFA-uri cu dosarul aprobat dar onboarding neterminat (nu mai sunt „înrolați" prematur).
    int InOnboarding = 0,
    // Conturi închise: nu intră la active sau inactive, dar datele lor rămân.
    int Deleted = 0);

/// <summary>
/// Firmele (SRL): câte sunt și în ce stare, plus venitul lor — abonamentul de flotă și anunțurile
/// plătite peste cele incluse. Aceleași definiții ca în lista „SRL înrolate”.
/// </summary>
public sealed record AdminSrlStats(
    int TotalEnrolled,
    int Active,
    int Inactive,
    int Deleted,
    int InOnboarding,
    int FailedPayment,
    long SubscriptionMonthlyRevenueBani,
    int CarsTotal,
    int CarsPublished,
    /// <summary>Anunțuri plătite separat, peste cele incluse în abonament, active acum.</summary>
    int PaidExtraListings,
    /// <summary>Încasările din anunțuri plătite separat, în perioada aleasă.</summary>
    long ExtraListingsRevenueBani,
    int ExtraListingsPayments);

public sealed record AdminOverviewPfaCard(
    Guid Id,
    Guid UserId,
    string CompanyName,
    string HolderName,
    string Email,
    string Phone,
    string Plan,
    string SubscriptionStatus,
    string CustomerAgeLabel,
    string AccountStatus,
    string CurrentMonthStatus,
    string LastActivityLabel,
    DateTime? LastActivityAtUtc);

public sealed record AdminPfaDetailResponse(
    Guid Id,
    Guid UserId,
    string CompanyName,
    string HolderName,
    string Email,
    string Phone,
    string AccountStatus,
    string Plan,
    string SubscriptionStatus,
    string RegistrationType,
    string CurrentMonthStatus,
    string LastActivityLabel,
    long? PriceBani,
    DateTime? SubscriptionStartedAtUtc,
    DateTime? NextPaymentAtUtc,
    DateTime? LastPaymentAtUtc,
    int FailedPayments,
    string? ActiveDiscount,
    string CustomerAgeLabel,
    string? LastProcessedMonth,
    int MissingMonthlyDocuments,
    int DocumentsToReview,
    string InternalNote,
    IReadOnlyList<AdminPfaActivityLogRow> ActivityLog,
    /// <summary>Clientul a bifat contul BCR la checkout — adminul are ce confirma.</summary>
    bool BcrDiscountRequested = false,
    /// <summary>Când s-a confirmat. Null cât timp reducerea n-a pornit.</summary>
    DateTime? BcrDiscountConfirmedAtUtc = null);

public sealed record AdminPfaActivityLogRow(
    Guid Id,
    string Description,
    DateTime CreatedAtUtc,
    string PerformedBy);

public static class AdminBillingLabels
{
    /// <summary>
    /// Cât costă planul pe ciclul lui. Sumele vin din <see cref="Pricing.Plans"/>, nu scrise a
    /// doua oară aici: până acum tabelul ăsta ținea prețurile săptămânale, iar estimarea lunară
    /// era „×4" — adică adminul raporta alte sume decât cele încasate.
    /// </summary>
    public static long PriceBani(SubscriptionPlan plan, SubscriptionBillingCycle cycle) =>
        (plan, cycle) switch
        {
            (SubscriptionPlan.Solo, SubscriptionBillingCycle.Annual) => Pricing.Plans.SoloAnnualBani,
            (SubscriptionPlan.Start, SubscriptionBillingCycle.Annual) => Pricing.Plans.StartAnnualBani,
            (SubscriptionPlan.Pro, SubscriptionBillingCycle.Annual) => Pricing.Plans.ProAnnualBani,
            (SubscriptionPlan.Fleet, SubscriptionBillingCycle.Annual) => Domain.Companies.FleetPricing.AnnualBani,
            (SubscriptionPlan.Fleet, _) => Domain.Companies.FleetPricing.MonthlyBani,
            (SubscriptionPlan.Solo, _) => Pricing.Plans.SoloMonthlyBani,
            (SubscriptionPlan.Start, _) => Pricing.Plans.StartMonthlyBani,
            (SubscriptionPlan.Pro, _) => Pricing.Plans.ProMonthlyBani,
            _ => 0
        };

    /// <summary>
    /// Contribuția lunară a unui abonament la venitul recurent. Un abonament anual se împarte la
    /// 12 — altfel o singură vânzare anuală ar umfla luna în care s-a făcut.
    /// </summary>
    public static long MonthlyEstimateBani(SubscriptionPlan plan, SubscriptionBillingCycle cycle) =>
        cycle == SubscriptionBillingCycle.Annual
            ? PriceBani(plan, cycle) / 12
            : PriceBani(plan, cycle);

    public static string PlanLabel(SubscriptionPlan? plan) => plan switch
    {
        SubscriptionPlan.Solo => "Solo",
        SubscriptionPlan.Start => "Start",
        SubscriptionPlan.Pro => "Pro",
        SubscriptionPlan.Fleet => "Fleet",
        _ => "Fără plan"
    };
}
