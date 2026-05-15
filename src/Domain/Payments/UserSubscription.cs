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
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.ActivePendingBilling;

    /// <summary>Stripe subscription ID (sub_xxx)</summary>
    public string? StripeSubscriptionId { get; set; }

    /// <summary>Stripe customer ID (cus_xxx)</summary>
    public string? StripeCustomerId { get; set; }

    /// <summary>
    /// The Monday 15:00 Romania time (stored as UTC) when the first automatic billing fires.
    /// Until this date, status is ActivePendingBilling.
    /// </summary>
    public DateTime FirstBillingDateUtc { get; set; }

    /// <summary>Next billing date (Monday 15:00 Romania time, stored as UTC)</summary>
    public DateTime? NextBillingDateUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAtUtc { get; set; }

    // Navigation
    public User User { get; set; } = null!;
}
