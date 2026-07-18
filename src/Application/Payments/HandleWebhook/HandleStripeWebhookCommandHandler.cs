using Application.Abstractions;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Application.Abstractions.Services;
using Domain.Cars;
using Domain.Notifications;
using Domain.Payments;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    IStripeService stripeService,
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer,
    IWebPushService webPushService,
    IInvoiceGenerator invoiceGenerator,
    IConfiguration configuration,
    ILogger<HandleStripeWebhookCommandHandler> logger)
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
            logger.LogWarning("Stripe webhook signature validation failed. Please check that Stripe__WebhookSecret in your .env matches the webhook secret from your stripe listen CLI command.");
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

        string? serviceOrderIdStr = session.Metadata?.GetValueOrDefault("serviceOrderId");
        if (Guid.TryParse(serviceOrderIdStr, out Guid serviceOrderId))
        {
            await HandlePublicServiceOrderCompleted(session, serviceOrderId, ct);
            return;
        }

        string? userIdStr = session.Metadata?.GetValueOrDefault("userId");
        if (!Guid.TryParse(userIdStr, out Guid userId))
        {
            return;
        }

        string mode = session.Mode; // "payment" or "subscription"

        if (session.Metadata?.GetValueOrDefault("paymentKind") == "car_listing" ||
            Guid.TryParse(session.Metadata?.GetValueOrDefault("carId"), out _))
        {
            await HandleCarListingCheckoutCompleted(session, userId, ct);
            return;
        }

        Guid? paymentRecordIdForInvoice = null;

        if (mode == "payment")
        {
            // One-time payment completed (e.g. Înființare PFA)
            string? planStr = session.Metadata?.GetValueOrDefault("customMetadata") ?? string.Empty;
            string description = planStr switch
            {
                var s when s.Contains("infiintare_pfa", StringComparison.OrdinalIgnoreCase) => "Înființare PFA",
                var s when s.Contains("sediu_social", StringComparison.OrdinalIgnoreCase) => "Găzduire Sediu Social",
                var s when s.Contains("start_ride", StringComparison.OrdinalIgnoreCase) => "Start Ride",
                _ => BuildDescriptionFromSession(session)
            };


            var record = new Domain.Payments.PaymentRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PaymentType = PaymentType.OneTime,
                Status = PaymentStatus.Succeeded,
                AmountBani = session.AmountTotal ?? 0,
                Description = description,
                StripePaymentId = session.PaymentIntentId,
                StripeSessionId = session.Id,
                CreatedAtUtc = DateTime.UtcNow,
            };
            context.PaymentRecords.Add(record);
            paymentRecordIdForInvoice = record.Id;
        }

        else if (mode == "subscription")
        {
            // Subscription checkout completed — record first payment + create subscription record
            string? planStr = session.Metadata?.GetValueOrDefault("customMetadata") ?? string.Empty;
            SubscriptionPlan plan = ParsePlan(planStr);

            DateTime firstBilling = GetNextMondayBillingDateUtc();

            // The account is usable immediately after the subscription checkout.
            // Monday 15:00 remains the automatic billing anchor, not an access gate.
            bool grantAccessNow = true;
            DateTime? accessGrantedUtc = DateTime.UtcNow;

            // Check if subscription already exists for this user (e.g. upgrade)
            UserSubscription? existing = await context.UserSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId, ct);

            if (existing is not null)
            {
                bool isPlanChange = session.Metadata?.GetValueOrDefault("isPlanChange") == "true";
                bool preserveDashboardAccess = isPlanChange && existing.DashboardAccessGranted;

                if (isPlanChange)
                {
                    existing.PendingPlan = plan;
                }
                else
                {
                    existing.Plan = plan;
                    existing.PendingPlan = null;
                }
                existing.Status = SubscriptionStatus.ActivePendingBilling;
                existing.StripeSubscriptionId = session.SubscriptionId;
                existing.StripeCustomerId = session.CustomerId;
                existing.FirstBillingDateUtc = firstBilling;
                existing.NextBillingDateUtc = firstBilling;
                existing.CancelledAtUtc = null;

                if (!preserveDashboardAccess)
                {
                    existing.DashboardAccessGranted = grantAccessNow;
                    existing.DashboardAccessGrantedUtc = accessGrantedUtc;
                }
            }
            else
            {
                var sub = new UserSubscription
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Plan = plan,
                    PendingPlan = null,
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
            paymentRecordIdForInvoice = record.Id;
        }

        await context.SaveChangesAsync(ct);

        // Generate Oblio invoice (never throws; failures are stored for admin review)
        if (paymentRecordIdForInvoice.HasValue)
        {
            await invoiceGenerator.GenerateForPaymentRecordAsync(paymentRecordIdForInvoice.Value, ct);
        }

        // Send payment confirmation email
        User? user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user != null)
        {
            decimal amountLei = (session.AmountTotal ?? 0) / 100m;
            string description = mode == "payment"
                ? BuildOneTimeDescription(session)
                : $"Abonament săptămânal RIDElance — Plan {session.Metadata?.GetValueOrDefault("customMetadata") ?? "Start"}";

            string mjml = ServiceOrderConfirmationEmail.BuildMjml(
                $"{user.FirstName} {user.LastName}",
                description,
                amountLei);
            string html = mjmlRenderer.Render(mjml);

            try
            {
                await emailService.SendEmailAsync(
                    user.Email,
                    ServiceOrderConfirmationEmail.Subject,
                    html,
                    ct);
            }
            catch
            {
                // Ignore email failure
            }

            // Send in-app and push notifications
            string descriptionForNotification = mode == "payment"
                ? BuildOneTimeDescription(session)
                : $"Plan {session.Metadata?.GetValueOrDefault("customMetadata") ?? "Start"}";

            await SendPaymentNotificationsAsync(userId, descriptionForNotification, amountLei, mode, ct);
        }
    }

    private async Task HandleCarListingCheckoutCompleted(Session session, Guid userId, CancellationToken ct)
    {
        if (!Guid.TryParse(session.Metadata?.GetValueOrDefault("carId"), out Guid carId))
        {
            return;
        }

        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.Id == carId && c.PostedByUserId == userId, ct);

        if (car is null)
        {
            return;
        }

        car.PaymentStatus = CarListingPaymentStatus.Paid;
        car.StripeCheckoutSessionId = session.Id;
        car.StripeSubscriptionId = session.SubscriptionId;
        car.PaidAtUtc = DateTime.UtcNow;
        car.Active = car.ApprovalStatus == CarApprovalStatus.Approved;
        car.UpdatedAtUtc = DateTime.UtcNow;

        bool paymentRecordExists = await context.PaymentRecords
            .AnyAsync(p => p.StripeSessionId == session.Id, ct);

        Guid? carPaymentRecordId = null;
        if (!paymentRecordExists)
        {
            var carRecord = new Domain.Payments.PaymentRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PaymentType = PaymentType.Subscription,
                Status = PaymentStatus.Succeeded,
                AmountBani = session.AmountTotal ?? 0,
                Description = $"Publicare mașină RIDElance — {car.Brand} {car.Model}",
                StripePaymentId = session.PaymentIntentId,
                StripeSessionId = session.Id,
                CreatedAtUtc = DateTime.UtcNow,
            };
            context.PaymentRecords.Add(carRecord);
            carPaymentRecordId = carRecord.Id;
        }

        await context.SaveChangesAsync(ct);

        if (carPaymentRecordId.HasValue)
        {
            await invoiceGenerator.GenerateForPaymentRecordAsync(carPaymentRecordId.Value, ct);
        }

        User? user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is not null)
        {
            decimal amountLei = (session.AmountTotal ?? 0) / 100m;
            string description = $"Publicare mașină RIDElance — {car.Brand} {car.Model}";

            try
            {
                string mjml = ServiceOrderConfirmationEmail.BuildMjml(
                    $"{user.FirstName} {user.LastName}",
                    description,
                    amountLei);
                string html = mjmlRenderer.Render(mjml);

                await emailService.SendEmailAsync(
                    user.Email,
                    ServiceOrderConfirmationEmail.Subject,
                    html,
                    ct);
            }
            catch
            {
                // Ignore email failure
            }

            await SendPaymentNotificationsAsync(userId, description, amountLei, "payment", ct);
        }
    }

    private async Task HandlePublicServiceOrderCompleted(Session session, Guid serviceOrderId, CancellationToken ct)
    {
        ServiceOrder? order = await context.ServiceOrders
            .FirstOrDefaultAsync(o => o.Id == serviceOrderId, ct);

        if (order is null || order.Status == ServiceOrderStatus.Paid)
        {
            return;
        }

        order.Status = ServiceOrderStatus.Paid;
        order.StripeSessionId = session.Id;
        order.AmountBani = session.AmountTotal;
        order.PaidAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(ct);

        await invoiceGenerator.GenerateForServiceOrderAsync(order.Id, ct);

        decimal amountLei = (session.AmountTotal ?? 0) / 100m;
        string mjml = ServiceOrderConfirmationEmail.BuildMjml(
            order.CustomerName,
            order.ServiceTitle,
            amountLei);
        string html = mjmlRenderer.Render(mjml);

        await emailService.SendEmailAsync(
            order.CustomerEmail,
            ServiceOrderConfirmationEmail.Subject,
            html,
            ct);
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

        Car? paidCar = await context.Cars
            .FirstOrDefaultAsync(c => c.StripeSubscriptionId == stripeSubId, ct);

        if (paidCar is not null)
        {
            paidCar.PaymentStatus = CarListingPaymentStatus.Paid;
            paidCar.Active = paidCar.ApprovalStatus == CarApprovalStatus.Approved;
            paidCar.UpdatedAtUtc = DateTime.UtcNow;

            var carPaymentRecord = new Domain.Payments.PaymentRecord
            {
                Id = Guid.NewGuid(),
                UserId = paidCar.PostedByUserId!.Value,
                PaymentType = PaymentType.Subscription,
                Status = PaymentStatus.Succeeded,
                AmountBani = invoice.AmountPaid,
                Description = $"Publicare mașină RIDElance — {paidCar.Brand} {paidCar.Model}",
                StripePaymentId = invoice.Payments?.FirstOrDefault()?.Payment?.PaymentIntentId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            context.PaymentRecords.Add(carPaymentRecord);

            await context.SaveChangesAsync(ct);

            await invoiceGenerator.GenerateForPaymentRecordAsync(carPaymentRecord.Id, ct);

            User? carPoster = await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == paidCar.PostedByUserId!.Value, ct);

            if (carPoster is not null)
            {
                decimal amountLei = invoice.AmountPaid / 100m;
                string description = $"Publicare mașină RIDElance — {paidCar.Brand} {paidCar.Model}";

                try
                {
                    string mjml = ServiceOrderConfirmationEmail.BuildMjml(
                        $"{carPoster.FirstName} {carPoster.LastName}",
                        description,
                        amountLei);
                    string html = mjmlRenderer.Render(mjml);

                    await emailService.SendEmailAsync(
                        carPoster.Email,
                        ServiceOrderConfirmationEmail.Subject,
                        html,
                        ct);
                }
                catch
                {
                    // Ignore email failure
                }

                await SendPaymentNotificationsAsync(paidCar.PostedByUserId.Value, description, amountLei, "payment", ct);
            }

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

        if (sub.PendingPlan.HasValue)
        {
            sub.Plan = sub.PendingPlan.Value;
            sub.PendingPlan = null;
        }

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

        await invoiceGenerator.GenerateForPaymentRecordAsync(record.Id, ct);

        // Send payment confirmation email
        User? user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == sub.UserId, ct);

        if (user != null)
        {
            decimal amountLei = invoice.AmountPaid / 100m;
            string description = $"Abonament săptămânal RIDElance — Plan {sub.Plan}";

            string mjml = ServiceOrderConfirmationEmail.BuildMjml(
                $"{user.FirstName} {user.LastName}",
                description,
                amountLei);
            string html = mjmlRenderer.Render(mjml);

            try
            {
                await emailService.SendEmailAsync(
                    user.Email,
                    ServiceOrderConfirmationEmail.Subject,
                    html,
                    ct);
            }
            catch
            {
                // Ignore email failure
            }

            // Send in-app and push notifications
            await SendPaymentNotificationsAsync(sub.UserId, $"Plan {sub.Plan}", amountLei, "subscription", ct);
        }
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

        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.StripeSubscriptionId == stripeSubId, ct);

        if (car is not null)
        {
            car.PaymentStatus = CarListingPaymentStatus.PastDue;
            car.Active = false;
            car.UpdatedAtUtc = DateTime.UtcNow;

            context.PaymentRecords.Add(new Domain.Payments.PaymentRecord
            {
                Id = Guid.NewGuid(),
                UserId = car.PostedByUserId!.Value,
                PaymentType = PaymentType.Subscription,
                Status = PaymentStatus.Failed,
                AmountBani = invoice.AmountDue,
                Description = $"Publicare mașină RIDElance — plată eșuată pentru {car.Brand} {car.Model}",
                StripePaymentId = invoice.Payments?.FirstOrDefault()?.Payment?.PaymentIntentId,
                CreatedAtUtc = DateTime.UtcNow,
            });

            await context.SaveChangesAsync(ct);
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

        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.StripeSubscriptionId == stripeSubscription.Id, ct);

        if (car is not null)
        {
            car.PaymentStatus = CarListingPaymentStatus.Cancelled;
            car.Active = false;
            car.UpdatedAtUtc = DateTime.UtcNow;

            await context.SaveChangesAsync(ct);
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

    private static string BuildOneTimeDescription(Session session)
    {
        string? metadata = session.Metadata?.GetValueOrDefault("customMetadata");
        if (!string.IsNullOrWhiteSpace(metadata))
        {
            if (metadata.Contains("infiintare_pfa", StringComparison.OrdinalIgnoreCase))
            {
                return "Înființare PFA";
            }

            if (metadata.Contains("sediu_social", StringComparison.OrdinalIgnoreCase))
            {
                return "Găzduire Sediu Social";
            }

            if (metadata.Contains("start_ride", StringComparison.OrdinalIgnoreCase))
            {
                return "Start Ride";
            }
        }

        return BuildDescriptionFromSession(session);
    }

    private async Task SendPaymentNotificationsAsync(Guid userId, string description, decimal amountLei, string mode, CancellationToken ct)
    {
        // 1. Create In-App Notification
        string notificationText = mode == "payment"
            ? $"Plată confirmată: {description} ({amountLei:N2} lei)."
            : $"Plată confirmată: abonament {description} ({amountLei:N2} lei).";

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Text = notificationText,
            Type = NotificationTypes.PaymentConfirmed,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Notifications.Add(notification);
        await context.SaveChangesAsync(ct);

        // 2. Load User with PushSubscriptions to send Push
        User? user = await context.Users
            .Include(u => u.PushSubscriptions)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user != null)
        {
            string pushTitle = "Plată confirmată";
            string pushBody = mode == "payment"
                ? $"Plata pentru {description} a fost înregistrată."
                : $"Plata pentru abonament a fost efectuată.";

            // Keep push body very short (under 50 chars)
            if (pushBody.Length > 50)
            {
                pushBody = pushBody[..47] + "...";
            }

            Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
            string relativePath = "/app/dashboard?section=istoric_plati";
            string deepLink = appBaseUri is null ? relativePath : new Uri(appBaseUri, relativePath).ToString();

            foreach (PushSubscription sub in user.PushSubscriptions)
            {
                try
                {
                    await webPushService.SendPushNotificationAsync(sub, pushTitle, pushBody, deepLink, ct);
                }
                catch
                {
                    // Ignore push failures
                }
            }
        }
    }
}
