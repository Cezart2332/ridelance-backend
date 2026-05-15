using Domain.Users;
using SharedKernel;

namespace Domain.Payments;

/// <summary>
/// Records a single payment event (subscription invoice or one-time charge).
/// </summary>
public sealed class PaymentRecord : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public PaymentType PaymentType { get; set; }
    public PaymentStatus Status { get; set; }

    /// <summary>Amount in bani (RON minor units, e.g. 4900 = 49 lei)</summary>
    public long AmountBani { get; set; }

    /// <summary>Human-readable description, e.g. "RIDElance Start — abonament săptămânal"</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Stripe PaymentIntent or Invoice ID</summary>
    public string? StripePaymentId { get; set; }

    /// <summary>Stripe Checkout Session ID</summary>
    public string? StripeSessionId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}
