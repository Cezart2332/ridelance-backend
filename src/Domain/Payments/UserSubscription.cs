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

    // Navigation
    public User User { get; set; } = null!;
}
