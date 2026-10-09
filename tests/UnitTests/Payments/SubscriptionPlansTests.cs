using Domain.Payments;
using Shouldly;
using Xunit;

namespace UnitTests.Payments;

/// <summary>Planurile PFAlone / PFA Full și opțiunile PFAlone, cum se cumpără.</summary>
public sealed class SubscriptionPlansTests
{
    [Theory]
    [InlineData("pfalone", SubscriptionBillingCycle.Monthly, 13_900L)]
    [InlineData("pfalone", SubscriptionBillingCycle.Annual, 150_120L)]
    [InlineData("pfa-full", SubscriptionBillingCycle.Monthly, 29_900L)]
    [InlineData("pfa-full", SubscriptionBillingCycle.Annual, 322_920L)]
    public void Plans_ResolveToTheAdvertisedPrices(string plan, SubscriptionBillingCycle cycle, long expectedBani)
    {
        StripeCatalog.TryResolvePlan(plan, "subscription", cycle, out StripeCatalogItem? item).ShouldBeTrue();
        item!.UnitAmountBani.ShouldBe(expectedBani);
        item.LookupKey.ShouldContain((expectedBani / 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("solo")]
    [InlineData("start")]
    [InlineData("pro")]
    public void OldPlans_CanNoLongerBeBought(string plan) =>
        StripeCatalog.TryResolvePlan(plan, "subscription", SubscriptionBillingCycle.Monthly, out _).ShouldBeFalse();

    [Fact]
    public void Addons_AreSoldOnlyWithPfaAlone_OnThePlansCycle()
    {
        StripeCatalog.TryResolveAddons("pfalone", ["open-banking", "cash-register"], SubscriptionBillingCycle.Monthly,
            out IReadOnlyList<(SubscriptionAddon Addon, StripeCatalogItem Item)> monthly).ShouldBeTrue();
        monthly.Select(a => a.Item.UnitAmountBani).ShouldBe([4_900L, 4_900L]);
        monthly.ShouldAllBe(a => a.Item.Interval == "month");

        StripeCatalog.TryResolveAddons("pfalone", ["open-banking"], SubscriptionBillingCycle.Annual,
            out IReadOnlyList<(SubscriptionAddon Addon, StripeCatalogItem Item)> annual).ShouldBeTrue();
        annual.Single().Item.Interval.ShouldBe("year");
        annual.Single().Item.UnitAmountBani.ShouldBe(52_920L);

        // La PFA Full sunt incluse: o cerere cu opțiuni e o greșeală, nu se ignoră tăcut.
        StripeCatalog.TryResolveAddons("pfa-full", ["open-banking"], SubscriptionBillingCycle.Monthly, out _).ShouldBeFalse();
        StripeCatalog.TryResolveAddons("pfalone", ["necunoscut"], SubscriptionBillingCycle.Monthly, out _).ShouldBeFalse();
    }

    [Fact]
    public void PfaFull_IncludesBothAddons_PfaAloneOnlyWhatItPaidFor()
    {
        new UserSubscription { Plan = SubscriptionPlan.PfaFull }.IncludesOpenBanking.ShouldBeTrue();
        new UserSubscription { Plan = SubscriptionPlan.PfaFull }.IncludesCashRegister.ShouldBeTrue();

        var alone = new UserSubscription { Plan = SubscriptionPlan.PfaAlone, HasOpenBankingAddon = true };
        alone.IncludesOpenBanking.ShouldBeTrue();
        alone.IncludesCashRegister.ShouldBeFalse();
    }

    /// <summary><c>pending_plan</c> se salvează ca număr: valorile vechi trebuie să rămână ale lor.</summary>
    [Fact]
    public void PlanValues_StayStable()
    {
        ((int)SubscriptionPlan.PfaAlone).ShouldBe(0);
        ((int)SubscriptionPlan.PfaFull).ShouldBe(1);
        ((int)SubscriptionPlan.Fleet).ShouldBe(3);
    }
}
