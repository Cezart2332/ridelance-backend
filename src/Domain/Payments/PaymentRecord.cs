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

    /// <summary>
    /// Dosarul PFA pentru care s-a plătit, când plata e legată de unul (taxa de înființare).
    /// Înainte legătura se ghicea din <see cref="Description"/> și <see cref="AmountBani"/>, ceea
    /// ce însemna că o schimbare de text sau de preț rupea în tăcere poarta de plată.
    /// </summary>
    public Guid? PfaRegistrationId { get; set; }

    /// <summary>Factura Stripe (in_xxx) a unei încasări de abonament, reușite sau nu.</summary>
    public string? StripeInvoiceId { get; set; }

    /// <summary>
    /// A câta încercare de încasare a facturii (<c>attempt_count</c> la Stripe). Fiecare încercare
    /// eșuată e un rând: așa se vede, la plăți eșuate, de ce a refuzat de fiecare dată.
    /// </summary>
    public int? AttemptNumber { get; set; }

    /// <summary>Codul Stripe al refuzului: <c>insufficient_funds</c>, <c>do_not_honor</c>…</summary>
    public string? FailureCode { get; set; }

    /// <summary>Motivul refuzului, pe înțelesul adminului și al clientului.</summary>
    public string? FailureReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}
