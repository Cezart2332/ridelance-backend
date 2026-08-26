using System.Diagnostics.CodeAnalysis;

namespace Domain.Payments;

/// <summary>
/// A purchasable item described independently of any Stripe account.
/// The lookup key is what makes it portable: the price is found by it, or created on first use,
/// so swapping the Stripe keys (another account, or test to live) needs no configuration change.
/// </summary>
/// <param name="LookupKey">Stable identifier searched for (and stamped on) the Stripe price.</param>
/// <param name="ProductName">Product name shown in Stripe; kept without diacritics like the existing ones.</param>
/// <param name="UnitAmountBani">Amount in the smallest currency unit (bani).</param>
/// <param name="Currency">ISO currency code, lowercase.</param>
/// <param name="Interval">Recurring interval ("month", "year"); <see langword="null"/> for a one-time price.</param>
/// <param name="Metadata">Metadata written on the product and the price at creation time.</param>
public sealed record StripeCatalogItem(
    string LookupKey,
    string ProductName,
    long UnitAmountBani,
    string Currency,
    string? Interval,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// Single source of truth for everything that can be bought.
/// Amounts live here, in git, instead of in per-environment configuration.
/// </summary>
/// <remarks>
/// A Stripe price is immutable: to change an amount you must also change the lookup key
/// (e.g. append "_v2"), otherwise the old price keeps being found and the new amount has no effect.
/// </remarks>
public static class StripeCatalog
{
    private const string Ron = "ron";

    /// <summary>
    /// Un plan de abonament, în ambele cicluri de facturare.
    /// </summary>
    /// <remarks>
    /// Cheile poartă suma („_199_”, „_2149_”) pentru că un <c>Price</c> Stripe e imutabil: dacă
    /// suma se schimbă, cheia trebuie să se schimbe odată cu ea, altfel se regăsește prețul vechi.
    /// Vechile chei săptămânale (<c>ridelance_plan_solo_weekly_ron</c> ș.a.) nu se mai caută —
    /// prețurile lor rămân în cont, dar nimeni nu le mai cumpără.
    /// </remarks>
    private static StripeCatalogItem Plan(string key, string title, long monthlyBani, long annualBani, bool annual) =>
        annual
            ? new StripeCatalogItem(
                $"ridelance_plan_{key}_annual_{annualBani / 100}_ron",
                $"{title} - anual",
                annualBani,
                Ron,
                "year",
                new Dictionary<string, string>
                {
                    ["app"] = "ridelance",
                    ["kind"] = "subscription_plan",
                    ["plan"] = key,
                    ["billing_unit"] = "year",
                })
            : new StripeCatalogItem(
                $"ridelance_plan_{key}_monthly_{monthlyBani / 100}_ron",
                title,
                monthlyBani,
                Ron,
                "month",
                new Dictionary<string, string>
                {
                    ["app"] = "ridelance",
                    ["kind"] = "subscription_plan",
                    ["plan"] = key,
                    ["billing_unit"] = "month",
                });

    public static StripeCatalogItem Solo { get; } =
        Plan("solo", "RIDElance Solo", Pricing.Plans.SoloMonthlyBani, Pricing.Plans.SoloAnnualBani, annual: false);

    public static StripeCatalogItem SoloAnnual { get; } =
        Plan("solo", "RIDElance Solo", Pricing.Plans.SoloMonthlyBani, Pricing.Plans.SoloAnnualBani, annual: true);

    public static StripeCatalogItem Start { get; } =
        Plan("start", "RIDElance Start", Pricing.Plans.StartMonthlyBani, Pricing.Plans.StartAnnualBani, annual: false);

    public static StripeCatalogItem StartAnnual { get; } =
        Plan("start", "RIDElance Start", Pricing.Plans.StartMonthlyBani, Pricing.Plans.StartAnnualBani, annual: true);

    public static StripeCatalogItem Pro { get; } =
        Plan("pro", "RIDElance Pro", Pricing.Plans.ProMonthlyBani, Pricing.Plans.ProAnnualBani, annual: false);

    public static StripeCatalogItem ProAnnual { get; } =
        Plan("pro", "RIDElance Pro", Pricing.Plans.ProMonthlyBani, Pricing.Plans.ProAnnualBani, annual: true);

    /// <summary>
    /// The RIDElance Start advance paid during onboarding, before the file reaches the accounting
    /// partner. Amount lives in <see cref="Pricing.RidelanceStart.OnboardingAdvanceBani"/>; the
    /// lookup key carries it because a Stripe price cannot be re-priced in place.
    /// </summary>
    public static StripeCatalogItem RidelanceStartAdvance { get; } = new(
        "ridelance_start_avans_399_ron",
        "Abonament RIDElance Start - avans",
        Pricing.RidelanceStart.OnboardingAdvanceBani,
        Ron,
        null,
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "start_advance",
            ["billing_unit"] = "one_time",
            ["refundable"] = Pricing.RidelanceStart.OnboardingAdvanceIsRefundable ? "yes" : "no",
        });

    /// <summary>Standalone PFA setup, bought from the public services page without a subscription.</summary>
    public static StripeCatalogItem InfiintarePfaPublic { get; } = new(
        "ridelance_infiintare_pfa_public_450_ron",
        "Infiintare PFA RIDElance - serviciu separat",
        45000,
        Ron,
        null,
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "public_pfa_setup",
            ["billing_unit"] = "one_time",
        });

    /// <summary>Advertised as "449 lei / an" but charged as a single payment, as before this catalog existed.</summary>
    public static StripeCatalogItem SediuSocial { get; } = new(
        "ridelance_sediu_social_449_ron",
        "Gazduire Sediu Social RIDElance",
        44900,
        Ron,
        null,
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "sediu_social",
            ["billing_unit"] = "one_time",
        });

    public static StripeCatalogItem StartRide { get; } = new(
        "ridelance_start_ride_799_ron",
        "Start Ride RIDElance",
        79900,
        Ron,
        null,
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "start_ride",
            ["billing_unit"] = "one_time",
        });

    public static StripeCatalogItem CarListingMonthly { get; } = new(
        "ridelance_car_listing_monthly_ron",
        "Publicare masina RIDElance",
        3000,
        Ron,
        "month",
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "car_listing_subscription",
            ["audience"] = "car_poster",
            ["billing_unit"] = "posted_car",
        });

    /// <summary>Every item, for tooling that needs to walk the whole catalog.</summary>
    public static IReadOnlyList<StripeCatalogItem> All { get; } =
    [
        Solo,
        SoloAnnual,
        Start,
        StartAnnual,
        Pro,
        ProAnnual,
        RidelanceStartAdvance,
        InfiintarePfaPublic,
        SediuSocial,
        StartRide,
        CarListingMonthly,
    ];

    private static readonly Dictionary<string, StripeCatalogItem> MonthlyPlans =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["solo"] = Solo,
            ["start"] = Start,
            ["pro"] = Pro,
        };

    private static readonly Dictionary<string, StripeCatalogItem> AnnualPlans =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["solo"] = SoloAnnual,
            ["start"] = StartAnnual,
            ["pro"] = ProAnnual,
        };

    private static readonly Dictionary<string, StripeCatalogItem> DashboardServices =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["infiintare_pfa"] = RidelanceStartAdvance,
            ["sediu_social"] = SediuSocial,
            ["start_ride"] = StartRide,
        };

    private static readonly Dictionary<string, (StripeCatalogItem Item, string Title)> PublicServices =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["infiintare_pfa"] = (InfiintarePfaPublic, "Înființare PFA"),
            ["sediu_social"] = (SediuSocial, "Găzduire Sediu Social"),
            ["start_ride"] = (StartRide, "Start Ride"),
        };

    /// <summary>
    /// Resolves what an authenticated user is buying from the app: a subscription plan on the
    /// requested billing cycle when <paramref name="mode"/> is "subscription", otherwise a
    /// one-time service from the dashboard (where the cycle is meaningless and ignored).
    /// </summary>
    public static bool TryResolvePlan(
        string planKey,
        string mode,
        SubscriptionBillingCycle cycle,
        [NotNullWhen(true)] out StripeCatalogItem? item)
    {
        if (!string.Equals(mode, "subscription", StringComparison.OrdinalIgnoreCase))
        {
            return DashboardServices.TryGetValue(planKey ?? string.Empty, out item);
        }

        Dictionary<string, StripeCatalogItem> source =
            cycle == SubscriptionBillingCycle.Annual ? AnnualPlans : MonthlyPlans;

        return source.TryGetValue(planKey ?? string.Empty, out item);
    }

    /// <summary>
    /// Resolves a service bought from the public services page (no account, no subscription).
    /// PFA setup costs more here than through onboarding, hence a separate item.
    /// </summary>
    public static bool TryResolvePublicService(
        string serviceKey,
        [NotNullWhen(true)] out StripeCatalogItem? item,
        [NotNullWhen(true)] out string? title)
    {
        if (PublicServices.TryGetValue(serviceKey ?? string.Empty, out (StripeCatalogItem Item, string Title) entry))
        {
            item = entry.Item;
            title = entry.Title;
            return true;
        }

        item = null;
        title = null;
        return false;
    }
}
