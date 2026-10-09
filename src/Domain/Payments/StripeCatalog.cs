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
    public static StripeCatalogItem Fleet { get; } = Plan("fleet", "RIDElance Fleet", Domain.Companies.FleetPricing.MonthlyBani, Domain.Companies.FleetPricing.AnnualBani, false);
    public static StripeCatalogItem FleetAnnual { get; } = Plan("fleet", "RIDElance Fleet", Domain.Companies.FleetPricing.MonthlyBani, Domain.Companies.FleetPricing.AnnualBani, true);
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

    public static StripeCatalogItem PfaAlone { get; } =
        Plan("pfalone", "RIDElance PFAlone", Pricing.Plans.PfaAloneMonthlyBani, Pricing.Plans.PfaAloneAnnualBani, annual: false);

    public static StripeCatalogItem PfaAloneAnnual { get; } =
        Plan("pfalone", "RIDElance PFAlone", Pricing.Plans.PfaAloneMonthlyBani, Pricing.Plans.PfaAloneAnnualBani, annual: true);

    public static StripeCatalogItem PfaFull { get; } =
        Plan("pfa-full", "RIDElance PFA Full", Pricing.Plans.PfaFullMonthlyBani, Pricing.Plans.PfaFullAnnualBani, annual: false);

    public static StripeCatalogItem PfaFullAnnual { get; } =
        Plan("pfa-full", "RIDElance PFA Full", Pricing.Plans.PfaFullMonthlyBani, Pricing.Plans.PfaFullAnnualBani, annual: true);

    /// <summary>
    /// O opțiune a PFAlone, pe același ciclu ca planul: se adaugă ca linie separată pe abonamentul
    /// Stripe, deci se facturează și se anulează odată cu el.
    /// </summary>
    private static StripeCatalogItem Addon(string key, string title, long monthlyBani, long annualBani, bool annual) =>
        new(
            $"ridelance_addon_{key}_{(annual ? "annual" : "monthly")}_{(annual ? annualBani : monthlyBani) / 100}_ron",
            annual ? $"{title} - anual" : title,
            annual ? annualBani : monthlyBani,
            Ron,
            annual ? "year" : "month",
            new Dictionary<string, string>
            {
                ["app"] = "ridelance",
                ["kind"] = "subscription_addon",
                ["addon"] = key,
                ["billing_unit"] = annual ? "year" : "month",
            });

    public static StripeCatalogItem OpenBanking { get; } =
        Addon("open-banking", "RIDElance Open Banking", Pricing.Addons.OpenBankingMonthlyBani, Pricing.Addons.OpenBankingAnnualBani, annual: false);

    public static StripeCatalogItem OpenBankingAnnual { get; } =
        Addon("open-banking", "RIDElance Open Banking", Pricing.Addons.OpenBankingMonthlyBani, Pricing.Addons.OpenBankingAnnualBani, annual: true);

    public static StripeCatalogItem CashRegister { get; } =
        Addon("cash-register", "RIDElance Automatizare casa de marcat", Pricing.Addons.CashRegisterMonthlyBani, Pricing.Addons.CashRegisterAnnualBani, annual: false);

    public static StripeCatalogItem CashRegisterAnnual { get; } =
        Addon("cash-register", "RIDElance Automatizare casa de marcat", Pricing.Addons.CashRegisterMonthlyBani, Pricing.Addons.CashRegisterAnnualBani, annual: true);

    /// <summary>
    /// The advance paid during onboarding — the first month of PFA Full. Amount lives in
    /// <see cref="Pricing.OnboardingAdvance.OnboardingAdvanceBani"/>; the lookup key carries it
    /// because a Stripe price cannot be re-priced in place.
    /// </summary>
    public static StripeCatalogItem OnboardingAdvance { get; } = new(
        $"ridelance_avans_onboarding_{Pricing.OnboardingAdvance.OnboardingAdvanceBani / 100}_ron",
        "Abonament RIDElance - avans",
        Pricing.OnboardingAdvance.OnboardingAdvanceBani,
        Ron,
        null,
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "start_advance",
            ["billing_unit"] = "one_time",
            ["refundable"] = Pricing.OnboardingAdvance.OnboardingAdvanceIsRefundable ? "yes" : "no",
        });

    /// <summary>Standalone PFA setup, bought from the public services page without a subscription.</summary>
    public static StripeCatalogItem InfiintarePfaPublic { get; } = new(
        $"ridelance_infiintare_pfa_public_{Pricing.Services.InfiintarePfaBani / 100}_ron",
        "Infiintare PFA RIDElance - serviciu separat",
        Pricing.Services.InfiintarePfaBani,
        Ron,
        null,
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "public_pfa_setup",
            ["billing_unit"] = "one_time",
        });

    /// <summary>Advertised as a yearly fee but charged as a single payment, as before this catalog existed.</summary>
    public static StripeCatalogItem SediuSocial { get; } = new(
        $"ridelance_sediu_social_{Pricing.Services.SediuSocialAnnualBani / 100}_ron",
        "Gazduire Sediu Social RIDElance - anual",
        Pricing.Services.SediuSocialAnnualBani,
        Ron,
        null,
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "sediu_social",
            ["billing_unit"] = "one_time",
        });

    public static StripeCatalogItem StartRide { get; } = new(
        $"ridelance_start_ride_{Pricing.Services.StartRideBani / 100}_ron",
        "Start Ride RIDElance - cu PFA si TVA intracomunitar",
        Pricing.Services.StartRideBani,
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

    /// <summary>Anunț de flotă peste cele incluse în abonament. Lunar, per mașină.</summary>
    public static StripeCatalogItem ExtraListingMonthly { get; } = new(
        $"ridelance_srl_extra_listing_monthly_{Pricing.PaidExtras.ExtraListingMonthlyBani}_bani",
        "Anunt extra RIDElance",
        Pricing.PaidExtras.ExtraListingMonthlyBani,
        Ron,
        "month",
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "srl_extra_listing",
            ["audience"] = "car_poster",
            ["billing_unit"] = "posted_car",
        });

    /// <summary>Ascunderea numărului de înmatriculare în anunț. O singură dată, per mașină.</summary>
    public static StripeCatalogItem HiddenPlate { get; } = new(
        $"ridelance_srl_hidden_plate_{Pricing.PaidExtras.HiddenPlateBani}_bani",
        "Numar de inmatriculare ascuns",
        Pricing.PaidExtras.HiddenPlateBani,
        Ron,
        null,
        new Dictionary<string, string>
        {
            ["app"] = "ridelance",
            ["kind"] = "srl_hidden_plate",
            ["audience"] = "car_poster",
            ["billing_unit"] = "one_time",
        });

    /// <summary>Every item, for tooling that needs to walk the whole catalog.</summary>
    public static IReadOnlyList<StripeCatalogItem> All { get; } =
    [
        PfaAlone,
        PfaAloneAnnual,
        PfaFull,
        PfaFullAnnual,
        OpenBanking,
        OpenBankingAnnual,
        CashRegister,
        CashRegisterAnnual,
        OnboardingAdvance,
        InfiintarePfaPublic,
        SediuSocial,
        StartRide,
        CarListingMonthly,
        ExtraListingMonthly,
        HiddenPlate,
    ];

    private static readonly Dictionary<string, StripeCatalogItem> MonthlyPlans =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pfalone"] = PfaAlone,
            ["pfa-full"] = PfaFull,
        };

    private static readonly Dictionary<string, StripeCatalogItem> AnnualPlans =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pfalone"] = PfaAloneAnnual,
            ["pfa-full"] = PfaFullAnnual,
        };

    private static readonly Dictionary<string, StripeCatalogItem> DashboardServices =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["infiintare_pfa"] = OnboardingAdvance,
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
    /// Opțiunile cerute la checkout, pe ciclul planului. Doar PFAlone le cumpără: la PFA Full sunt
    /// incluse. O cheie necunoscută face toată cererea invalidă — nu se ignoră tăcut.
    /// </summary>
    public static bool TryResolveAddons(
        string planKey,
        IReadOnlyCollection<string> addonKeys,
        SubscriptionBillingCycle cycle,
        out IReadOnlyList<(SubscriptionAddon Addon, StripeCatalogItem Item)> addons)
    {
        var resolved = new List<(SubscriptionAddon, StripeCatalogItem)>();
        addons = resolved;

        if (addonKeys.Count == 0)
        {
            return true;
        }

        if (!string.Equals(planKey, "pfalone", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool annual = cycle == SubscriptionBillingCycle.Annual;
        foreach (string key in addonKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            switch (key.ToUpperInvariant())
            {
                case "OPEN-BANKING":
                    resolved.Add((SubscriptionAddon.OpenBanking, annual ? OpenBankingAnnual : OpenBanking));
                    break;
                case "CASH-REGISTER":
                    resolved.Add((SubscriptionAddon.CashRegister, annual ? CashRegisterAnnual : CashRegister));
                    break;
                default:
                    return false;
            }
        }

        return true;
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
