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
/// <param name="Interval">Recurring interval ("week", "month"); <see langword="null"/> for a one-time price.</param>
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

    public static StripeCatalogItem Solo { get; } = new(
        "ridelance_plan_solo_weekly_ron",
        "RIDElance Solo",
        4900,
        Ron,
        "week",
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "subscription_plan",
            ["plan"] = "solo",
            ["billing_unit"] = "week",
        });

    public static StripeCatalogItem Start { get; } = new(
        "ridelance_plan_start_weekly_ron",
        "RIDElance Start",
        9900,
        Ron,
        "week",
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "subscription_plan",
            ["plan"] = "start",
            ["billing_unit"] = "week",
        });

    public static StripeCatalogItem Pro { get; } = new(
        "ridelance_plan_pro_weekly_ron",
        "RIDElance Pro",
        14900,
        Ron,
        "week",
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "subscription_plan",
            ["plan"] = "pro",
            ["billing_unit"] = "week",
        });

    /// <summary>Preferential PFA setup fee, bought during onboarding.</summary>
    public static StripeCatalogItem InfiintarePfaOnboarding { get; } = new(
        "ridelance_infiintare_pfa_300_ron",
        "Infiintare PFA RIDElance",
        30000,
        Ron,
        null,
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "pfa_setup",
            ["billing_unit"] = "one_time",
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
        Start,
        Pro,
        InfiintarePfaOnboarding,
        InfiintarePfaPublic,
        SediuSocial,
        StartRide,
        CarListingMonthly,
    ];

    private static readonly Dictionary<string, StripeCatalogItem> SubscriptionPlans =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["solo"] = Solo,
            ["start"] = Start,
            ["pro"] = Pro,
        };

    private static readonly Dictionary<string, StripeCatalogItem> DashboardServices =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["infiintare_pfa"] = InfiintarePfaOnboarding,
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
    /// Resolves what an authenticated user is buying from the app: a weekly plan when
    /// <paramref name="mode"/> is "subscription", otherwise a one-time service from the dashboard.
    /// </summary>
    public static bool TryResolvePlan(string planKey, string mode, [NotNullWhen(true)] out StripeCatalogItem? item)
    {
        Dictionary<string, StripeCatalogItem> source =
            string.Equals(mode, "subscription", StringComparison.OrdinalIgnoreCase)
                ? SubscriptionPlans
                : DashboardServices;

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
