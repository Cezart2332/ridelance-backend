namespace Domain.Payments;

public enum SubscriptionStatus
{
    /// <summary>
    /// Subscription is active. Billing has started.
    /// </summary>
    Active,

    /// <summary>
    /// Subscription paid but not yet at the Monday 15:00 billing anchor.
    /// Account is usable, first automatic billing is pending.
    /// </summary>
    ActivePendingBilling,

    /// <summary>
    /// Subscription was cancelled.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Payment failed; subscription is past due.
    /// </summary>
    PastDue,

    /// <summary>
    /// Subscription has expired.
    /// </summary>
    Expired
}
