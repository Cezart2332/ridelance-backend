using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Stripe;
using Stripe.Checkout;

namespace Application.Payments.HandleWebhook;

/// <summary>
/// Handles Stripe webhook events and updates the database accordingly.
///
/// Events handled:
/// - checkout.session.completed   → record payment, create/update subscription
/// - invoice.payment_succeeded    → record recurring subscription payment
/// - invoice.payment_failed       → mark subscription as PastDue
/// - customer.subscription.deleted → cancel subscription
/// </summary>
internal sealed class HandleStripeWebhookCommandHandler(
    IApplicationDbContext context,
    IStripeService stripeService)
    : ICommandHandler<HandleStripeWebhookCommand>
{
    public async Task<Result> Handle(
        HandleStripeWebhookCommand command,
        CancellationToken cancellationToken)
    {
        Stripe.Event? stripeEvent = stripeService.ConstructWebhookEvent(
            command.Payload,
            command.StripeSignatureHeader);

        if (stripeEvent is null)
        {
            return Result.Failure(Error.Problem("Stripe.WebhookSignature", "Invalid Stripe webhook signature."));
        }

        await (stripeEvent.Type switch
        {
            "checkout.session.completed"    => HandleCheckoutSessionCompleted(stripeEvent, cancellationToken),
            "invoice.payment_succeeded"     => HandleInvoicePaymentSucceeded(stripeEvent, cancellationToken),
            "invoice.payment_failed"        => HandleInvoicePaymentFailed(stripeEvent, cancellationToken),
            "customer.subscription.deleted" => HandleSubscriptionDeleted(stripeEvent, cancellationToken),
            _                               => Task.CompletedTask,
        });

        return Result.Success();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // checkout.session.completed
    // ─────────────────────────────────────────────────────────────────────────
    private async Task HandleCheckoutSessionCompleted(Stripe.Event e, CancellationToken ct)
    {
        if (e.Data.Object is not Session session)
        {
            return;
        }

        string? userIdStr = session.Metadata?.GetValueOrDefault("userId");
        if (!Guid.TryParse(userIdStr, out Guid userId))
        {
            return;
        }

        string mode = session.Mode; // "payment" or "subscription"

        if (mode == "payment")
        {
            // One-time payment completed (e.g. Înființare PFA)
            var record = new Domain.Payments.PaymentRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PaymentType = PaymentType.OneTime,
                Status = PaymentStatus.Succeeded,
                AmountBani = session.AmountTotal ?? 0,
                Description = BuildDescriptionFromSession(session),
                StripePaymentId = session.PaymentIntentId,
                StripeSessionId = session.Id,
                CreatedAtUtc = DateTime.UtcNow,
            };
            context.PaymentRecords.Add(record);
        }
        else if (mode == "subscription")
        {
            // Subscription checkout completed — record first payment + create subscription record
            string? planStr = session.Metadata?.GetValueOrDefault("customMetadata") ?? string.Empty;
            SubscriptionPlan plan = ParsePlan(planStr);

            DateTime firstBilling = GetNextMondayBillingDateUtc();

            // Grant dashboard access immediately only if it's currently Monday at or after 15:00 Romania time.
            // Otherwise the Monday 15:00 background job will flip the flag.
            bool grantAccessNow = IsOnOrAfterMondayFifteenOClockRomania();
            DateTime? accessGrantedUtc = grantAccessNow ? DateTime.UtcNow : null;

            // Check if subscription already exists for this user (e.g. upgrade)
            UserSubscription? existing = await context.UserSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId, ct);

            if (existing is not null)
            {
                existing.Plan = plan;
                existing.Status = SubscriptionStatus.ActivePendingBilling;
                existing.StripeSubscriptionId = session.SubscriptionId;
                existing.StripeCustomerId = session.CustomerId;
                existing.FirstBillingDateUtc = firstBilling;
                existing.NextBillingDateUtc = firstBilling;
                existing.CancelledAtUtc = null;
                existing.DashboardAccessGranted = grantAccessNow;
                existing.DashboardAccessGrantedUtc = accessGrantedUtc;
            }
            else
            {
                var sub = new UserSubscription
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Plan = plan,
                    Status = SubscriptionStatus.ActivePendingBilling,
                    StripeSubscriptionId = session.SubscriptionId,
                    StripeCustomerId = session.CustomerId,
                    FirstBillingDateUtc = firstBilling,
                    NextBillingDateUtc = firstBilling,
                    CreatedAtUtc = DateTime.UtcNow,
                    DashboardAccessGranted = grantAccessNow,
                    DashboardAccessGrantedUtc = accessGrantedUtc,
                };
                context.UserSubscriptions.Add(sub);
            }

            // Record subscription payment
            var record = new Domain.Payments.PaymentRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PaymentType = PaymentType.Subscription,
                Status = PaymentStatus.Succeeded,
                AmountBani = session.AmountTotal ?? 0,
                Description = $"RIDElance {plan} — abonament săptămânal",
                StripePaymentId = session.PaymentIntentId,
                StripeSessionId = session.Id,
                CreatedAtUtc = DateTime.UtcNow,
            };
            context.PaymentRecords.Add(record);
        }

        await context.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // invoice.payment_succeeded (recurring billing)
    // ─────────────────────────────────────────────────────────────────────────
    private async Task HandleInvoicePaymentSucceeded(Stripe.Event e, CancellationToken ct)
    {
        if (e.Data.Object is not Stripe.Invoice invoice)
        {
            return;
        }

        if (invoice.BillingReason == "subscription_create")
        {
            return; // Already handled by checkout.session.completed
        }

        string? stripeSubId = invoice.Lines?.FirstOrDefault()?.SubscriptionId;
        if (string.IsNullOrEmpty(stripeSubId))
        {
            return;
        }

        UserSubscription? sub = await context.UserSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId, ct);

        if (sub is null)
        {
            return;
        }

        // Update to Active, set next billing date, and grant dashboard access
        // (recurring invoice means Monday job already ran or this is the first cycle)
        sub.Status = SubscriptionStatus.Active;
        sub.NextBillingDateUtc = GetNextMondayBillingDateUtc();
        sub.DashboardAccessGranted = true;
        sub.DashboardAccessGrantedUtc ??= DateTime.UtcNow;

        // Record the payment
        var record = new Domain.Payments.PaymentRecord
        {
            Id = Guid.NewGuid(),
            UserId = sub.UserId,
            PaymentType = PaymentType.Subscription,
            Status = PaymentStatus.Succeeded,
            AmountBani = invoice.AmountPaid,
            Description = $"RIDElance {sub.Plan} — abonament săptămânal",
            StripePaymentId = invoice.Payments?.FirstOrDefault()?.Payment?.PaymentIntentId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        context.PaymentRecords.Add(record);

        await context.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // invoice.payment_failed
    // ─────────────────────────────────────────────────────────────────────────
    private async Task HandleInvoicePaymentFailed(Stripe.Event e, CancellationToken ct)
    {
        if (e.Data.Object is not Stripe.Invoice invoice)
        {
            return;
        }

        string? stripeSubId = invoice.Lines?.FirstOrDefault()?.SubscriptionId;
        if (string.IsNullOrEmpty(stripeSubId))
        {
            return;
        }

        UserSubscription? sub = await context.UserSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubId, ct);

        if (sub is null)
        {
            return;
        }

        sub.Status = SubscriptionStatus.PastDue;

        var record = new Domain.Payments.PaymentRecord
        {
            Id = Guid.NewGuid(),
            UserId = sub.UserId,
            PaymentType = PaymentType.Subscription,
            Status = PaymentStatus.Failed,
            AmountBani = invoice.AmountDue,
            Description = $"RIDElance {sub.Plan} — plată eșuată",
            StripePaymentId = invoice.Payments?.FirstOrDefault()?.Payment?.PaymentIntentId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        context.PaymentRecords.Add(record);

        await context.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // customer.subscription.deleted
    // ─────────────────────────────────────────────────────────────────────────
    private async Task HandleSubscriptionDeleted(Stripe.Event e, CancellationToken ct)
    {
        if (e.Data.Object is not Subscription stripeSubscription)
        {
            return;
        }

        UserSubscription? sub = await context.UserSubscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscription.Id, ct);

        if (sub is null)
        {
            return;
        }

        sub.Status = SubscriptionStatus.Cancelled;
        sub.CancelledAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the next Monday 15:00 Romania time in UTC.
    /// Romania is EEST (+3) in summer, EET (+2) in winter.
    /// </summary>
    private static DateTime GetNextMondayBillingDateUtc()
    {
        // Use Romania timezone
        var romaniaZone = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime nowRomania = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, romaniaZone);

        int dayOfWeek = (int)nowRomania.DayOfWeek; // 0=Sun, 1=Mon
        // Always push to next week's Monday (business rule: even if today is Monday, next week)
        int daysUntilMonday = dayOfWeek == 1 ? 7 : (8 - dayOfWeek) % 7;
        if (daysUntilMonday == 0)
        {
            daysUntilMonday = 7;
        }

        DateTime nextMondayRomania = nowRomania.Date
            .AddDays(daysUntilMonday)
            .AddHours(15);

        return TimeZoneInfo.ConvertTimeToUtc(nextMondayRomania, romaniaZone);
    }

    /// <summary>
    /// Returns true if it is currently Monday at or after 15:00 Romania time.
    /// Used to immediately grant dashboard access if a user pays at the right moment.
    /// </summary>
    private static bool IsOnOrAfterMondayFifteenOClockRomania()
    {
        var romaniaZone = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime nowRomania = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, romaniaZone);
        return nowRomania.DayOfWeek == DayOfWeek.Monday
            && nowRomania.TimeOfDay >= TimeSpan.FromHours(15);
    }

    private static SubscriptionPlan ParsePlan(string metadata)
    {
        // metadata format: "plan:solo|billingAnchor:1234567"
        string planPart = metadata;
        int pipeIdx = metadata.IndexOf('|');
        if (pipeIdx > 0)
        {
            planPart = metadata[..pipeIdx];
        }

        if (planPart.Contains("solo", StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionPlan.Solo;
        }
        if (planPart.Contains("pro", StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionPlan.Pro;
        }
        return SubscriptionPlan.Start; // default
    }

    private static string BuildDescriptionFromSession(Session session)
    {
        return session.LineItems?.Data?.FirstOrDefault()?.Description
            ?? "Serviciu individual RIDElance";
    }
}
