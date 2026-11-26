namespace Domain.Payments;

/// <summary>
/// Ce facem când reînnoirea unui abonament nu se încasează (dunning).
///
/// Reîncercăm noi, nu Stripe: de 5 ori, la 3 zile una de alta, la aceeași oră ca încasarea care a
/// eșuat — deci 15 zile. Cât durează, clientul își păstrează accesul și vede de ce a refuzat banca.
/// Dacă și a cincea reîncercare pică, abonamentul se suspendă și se oprește la Stripe.
///
/// Reîncercările automate ale Stripe se opresc pe factura respectivă (<c>auto_advance = false</c>):
/// altfel ar încerca și ele, după setările contului Stripe, iar clientul ar fi taxat de două părți.
/// </summary>
public static class SubscriptionPaymentRetry
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(3);

    public const int MaxAttempts = 5;

    /// <summary>Cât ține totul, de la încasarea eșuată: 5 × 3 = 15 zile.</summary>
    public static readonly TimeSpan Window = Interval * MaxAttempts;

    /// <summary>Abonamentul e acum în reîncercări.</summary>
    public static bool IsRetrying(UserSubscription subscription) =>
        subscription.PaymentRetryInvoiceId is not null && subscription.NextPaymentRetryAtUtc is not null;

    /// <summary>Prima încasare a facturii a eșuat: pornesc cele 15 zile.</summary>
    public static void Start(UserSubscription subscription, string invoiceId, string? hostedInvoicePage, DateTime failedAtUtc, string? reason)
    {
        subscription.PaymentRetryInvoiceId = invoiceId;
        subscription.PaymentRetryInvoiceUrl = hostedInvoicePage;
        subscription.PaymentRetryStartedAtUtc = failedAtUtc;
        subscription.PaymentRetryAttempts = 0;
        subscription.NextPaymentRetryAtUtc = failedAtUtc + Interval;
        subscription.LastPaymentFailureReason = reason;
    }

    /// <summary>
    /// O reîncercare a picat. Următoarea vine la 3 zile de cea programată, nu de rularea jobului,
    /// ca ora să rămână aceeași. Întoarce <c>true</c> când a fost ultima: abonamentul se suspendă.
    /// </summary>
    public static bool RecordFailedAttempt(UserSubscription subscription, DateTime nowUtc)
    {
        subscription.PaymentRetryAttempts++;
        if (subscription.PaymentRetryAttempts < MaxAttempts)
        {
            subscription.NextPaymentRetryAtUtc = (subscription.NextPaymentRetryAtUtc ?? nowUtc) + Interval;
            return false;
        }

        subscription.NextPaymentRetryAtUtc = null;
        subscription.Status = SubscriptionStatus.Expired;
        subscription.CancelledAtUtc = nowUtc;
        return true;
    }

    /// <summary>Factura s-a plătit (sau nu mai e de plătit): abonamentul iese din reîncercări.</summary>
    public static void Clear(UserSubscription subscription)
    {
        subscription.PaymentRetryInvoiceId = null;
        subscription.PaymentRetryInvoiceUrl = null;
        subscription.PaymentRetryStartedAtUtc = null;
        subscription.PaymentRetryAttempts = 0;
        subscription.NextPaymentRetryAtUtc = null;
        subscription.LastPaymentFailureReason = null;
    }
}
