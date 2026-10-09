using Domain.Users;
using SharedKernel;

namespace Domain.Payments;

/// <summary>
/// Tracks a user's active Stripe subscription.
/// </summary>
public sealed class UserSubscription : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public SubscriptionPlan Plan { get; set; }
    public SubscriptionPlan? PendingPlan { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    /// <summary>
    /// Lunar sau anual. Se stabilește la checkout și nu se schimbă decât printr-un checkout nou:
    /// reînnoirea, prețul afișat și descrierea facturii se derivă din el.
    /// </summary>
    public SubscriptionBillingCycle BillingCycle { get; set; } = SubscriptionBillingCycle.Monthly;

    /// <summary>Opțiunea Open Banking, plătită pe abonament (PFAlone). La PFA Full e inclusă.</summary>
    public bool HasOpenBankingAddon { get; set; }

    /// <summary>Opțiunea de automatizare a casei de marcat, plătită pe abonament (PFAlone).</summary>
    public bool HasCashRegisterAddon { get; set; }

    /// <summary>
    /// PFAlone: până când Open Banking conectat în onboarding merge gratuit. După, clientul alege:
    /// îl plătește (opțiunea pe abonament) sau renunță (banca se deconectează).
    /// </summary>
    public DateTime? OpenBankingTrialEndsAtUtc { get; set; }

    /// <summary>PFAlone a refuzat Open Banking la finalul lunii gratuite.</summary>
    public DateTime? OpenBankingDeclinedAtUtc { get; set; }

    /// <summary>Luna gratuită de Open Banking.</summary>
    public const int OpenBankingTrialMonths = 1;

    /// <summary>
    /// Conectarea băncii e disponibilă: inclusă în PFA Full, plătită la PFAlone sau, la PFAlone,
    /// în luna gratuită.
    /// </summary>
    public bool IncludesOpenBankingAt(DateTime nowUtc) =>
        Plan == SubscriptionPlan.PfaFull
        || HasOpenBankingAddon
        || Plan == SubscriptionPlan.PfaAlone && OpenBankingDeclinedAtUtc is null && OpenBankingTrialEndsAtUtc > nowUtc;

    /// <summary>PFAlone, luna gratuită s-a terminat și n-a ales încă: îl întrebăm înainte de orice altceva.</summary>
    public bool OpenBankingDecisionDueAt(DateTime nowUtc) =>
        Plan == SubscriptionPlan.PfaAlone
        && !HasOpenBankingAddon
        && OpenBankingDeclinedAtUtc is null
        && OpenBankingTrialEndsAtUtc <= nowUtc;

    /// <summary>
    /// Pornește luna gratuită la primul abonament PFAlone fără opțiunea plătită. O singură dată:
    /// o schimbare de plan ulterioară nu mai dă încă o lună.
    /// </summary>
    public void StartOpenBankingTrial(DateTime nowUtc)
    {
        if (Plan == SubscriptionPlan.PfaAlone && !HasOpenBankingAddon && OpenBankingTrialEndsAtUtc is null)
        {
            OpenBankingTrialEndsAtUtc = nowUtc.AddMonths(OpenBankingTrialMonths);
        }
    }

    /// <summary>Automatizarea casei de marcat e disponibilă: inclusă în PFA Full, plătită la PFAlone.</summary>
    public bool IncludesCashRegister => Plan == SubscriptionPlan.PfaFull || HasCashRegisterAddon;

    /// <summary>Stripe subscription ID (sub_xxx)</summary>
    public string? StripeSubscriptionId { get; set; }

    /// <summary>Stripe customer ID (cus_xxx)</summary>
    public string? StripeCustomerId { get; set; }

    /// <summary>
    /// Când s-a încasat prima dată. Abonamentul se plătește la checkout, deci e chiar momentul
    /// plății — nu o dată viitoare. (Până acum aici stătea „lunea următoare la 15:00”, ancora
    /// artificială pe care o aștepta prima încasare.)
    /// </summary>
    public DateTime FirstBillingDateUtc { get; set; }

    /// <summary>Următoarea încasare: o lună sau un an de la ultima, după <see cref="BillingCycle"/>.</summary>
    public DateTime? NextBillingDateUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAtUtc { get; set; }

    /// <summary>
    /// Istoric. Accesul la dashboard nu mai depinde de nimic în afară de un abonament plătit, deci
    /// coloana nu mai poartă nicio decizie — se scrie la plată și rămâne acolo pentru rândurile
    /// vechi. Nu o citi ca poartă de acces: <c>canAccessDashboard</c> nu se mai uită la ea.
    /// </summary>
    public bool DashboardAccessGranted { get; set; }

    /// <summary>Istoric, pereche cu <see cref="DashboardAccessGranted"/>.</summary>
    public DateTime? DashboardAccessGrantedUtc { get; set; }

    /// <summary>
    /// Când a bifat clientul, la checkout, că își deschide cont BCR. Doar intenția — nu dovada.
    /// </summary>
    public DateTime? BcrDiscountRequestedAtUtc { get; set; }

    /// <summary>
    /// Când a confirmat BCR contul, marcat de un administrator. De aici pornesc cele
    /// <see cref="Pricing.BcrDiscount.Months"/> luni de reducere; până aici, bifa nu costă nimic
    /// pe nimeni.
    /// </summary>
    public DateTime? BcrDiscountConfirmedAtUtc { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
