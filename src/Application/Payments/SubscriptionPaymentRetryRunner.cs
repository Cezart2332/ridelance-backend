using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Domain.Notifications;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace Application.Payments;

/// <summary>
/// Reîncercările de încasare ale abonamentelor (<see cref="SubscriptionPaymentRetry"/>): ia
/// abonamentele cu reîncercarea scadentă și încearcă factura de pe cardul salvat.
///
/// Rândul de plată eșuată, cu motivul, îl scrie webhook-ul <c>invoice.payment_failed</c>, care vine
/// și pentru încercările noastre. Aici se decide doar ce urmează: altă reîncercare sau suspendarea.
/// </summary>
public sealed class SubscriptionPaymentRetryRunner(
    IApplicationDbContext context,
    IStripeService stripe,
    ILogger<SubscriptionPaymentRetryRunner> logger)
{
    /// <summary>
    /// O eroare care nu ține de card (Stripe căzut, rețea) nu consumă o încercare: se reia peste o
    /// oră, ca un incident de partea noastră să nu suspende un client care are bani pe card.
    /// </summary>
    public static readonly TimeSpan TechnicalErrorDelay = TimeSpan.FromHours(1);

    /// <returns>Câte abonamente au fost încercate.</returns>
    public async Task<int> RunDueAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        List<UserSubscription> due = await context.UserSubscriptions
            .Where(s => s.PaymentRetryInvoiceId != null && s.NextPaymentRetryAtUtc != null && s.NextPaymentRetryAtUtc <= nowUtc
                && s.Status == SubscriptionStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (UserSubscription subscription in due)
        {
            await RetryAsync(subscription, nowUtc, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    private async Task RetryAsync(UserSubscription subscription, DateTime nowUtc, CancellationToken cancellationToken)
    {
        string invoiceId = subscription.PaymentRetryInvoiceId!;
        InvoiceRetryResult result;
        try
        {
            result = await stripe.RetryInvoicePaymentAsync(invoiceId, cancellationToken);
        }
        catch (StripeException exception)
        {
            logger.LogWarning(exception, "Reîncercarea facturii {InvoiceId} n-a ajuns la bancă; o reluăm peste o oră.", invoiceId);
            subscription.NextPaymentRetryAtUtc = nowUtc + TechnicalErrorDelay;
            return;
        }

        switch (result.Outcome)
        {
            case InvoiceRetryOutcome.Paid:
            case InvoiceRetryOutcome.NotPayable:
                // Încasarea o înregistrează `invoice.payment_succeeded`; aici doar ieșim din reîncercări.
                SubscriptionPaymentRetry.Clear(subscription);
                return;
        }

        subscription.LastPaymentFailureReason = PaymentFailureReasons.Describe(result.Code, result.DeclineCode, result.Message);
        bool lastAttempt = SubscriptionPaymentRetry.RecordFailedAttempt(subscription, nowUtc);
        if (!lastAttempt)
        {
            return;
        }

        logger.LogInformation(
            "Abonamentul {SubscriptionId} se suspendă după {Attempts} reîncercări refuzate ({Reason}).",
            subscription.Id,
            SubscriptionPaymentRetry.MaxAttempts,
            subscription.LastPaymentFailureReason);

        // Oprit și la Stripe: altfel luna următoare ar emite o factură nouă, pe care ar încerca s-o
        // încaseze de la un client fără acces.
        if (!string.IsNullOrEmpty(subscription.StripeSubscriptionId))
        {
            try
            {
                await stripe.CancelSubscriptionAsync(subscription.StripeSubscriptionId, cancellationToken);
            }
            catch (StripeException exception)
            {
                logger.LogWarning(exception, "Abonamentul Stripe {StripeSubscriptionId} nu a putut fi oprit după suspendare.", subscription.StripeSubscriptionId);
            }
        }

        context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = subscription.UserId,
            Text = $"Am încercat de {SubscriptionPaymentRetry.MaxAttempts} ori să încasăm abonamentul, fără succes ({subscription.LastPaymentFailureReason}). Accesul a fost suspendat; îl reactivezi alegând din nou un abonament.",
            Type = NotificationTypes.PaymentFailed,
            IsRead = false,
            DedupeKey = $"payment-suspended:{invoiceId}",
            CreatedAtUtc = nowUtc,
        });
    }
}
