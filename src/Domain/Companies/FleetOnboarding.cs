using Domain.Payments;

namespace Domain.Companies;

public sealed class FleetOnboarding
{
    public int CompletedStep { get; set; }
    public FleetCompany? Company { get; set; }
    public FleetCompany? PendingCompany { get; set; }
    public string? Position { get; set; }
    public string[] Platforms { get; set; } = [];
    public int VehicleCount { get; set; }
    public bool BankDeferred { get; set; }
    public bool OblioDeferred { get; set; }
    public bool BcrRequested { get; set; }
    public DateTime? BcrEligibleAtUtc { get; set; }
    public SubscriptionBillingCycle Cycle { get; set; }
    public DateTime? TermsAcceptedAtUtc { get; set; }
    public string? LegalVersion { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? CheckoutClientSecret { get; set; }
    public Guid? CheckoutAttemptId { get; set; }
}

public sealed record FleetCompany(
    string Cui, string Name, string? Address, string? City, string? County,
    string? RegistrationNumber, bool VatPayer, string? PostalCode, string? Caen,
    string? RegistrationDate, string? Status, bool? VatOnCollection);

public static class FleetPricing
{
    public const long MonthlyBani = 29_900;
    public const long AnnualBani = 322_920;
    public const string LegalVersion = "2026-09-06";

    public static long AmountDue(SubscriptionBillingCycle cycle, bool bcrEligible)
    {
        bool annual = cycle == SubscriptionBillingCycle.Annual;
        long price = annual ? AnnualBani : MonthlyBani;
        long discount = annual ? 30_000 : 5_000;
        return price - (bcrEligible ? discount : 0);
    }

    public static Pricing.OnboardingAdvanceCredit.Spec BcrCoupon(SubscriptionBillingCycle cycle) =>
        cycle == SubscriptionBillingCycle.Annual
            ? new("ridelance_fleet_bcr_300ron_once", "Beneficiu BCR — 6 luni", 30_000, 1)
            : new("ridelance_fleet_bcr_50ron_6m", "Beneficiu BCR", 5_000, 6);
}
