using System.Reflection;
using Application.Abstractions;
using Application.Abstractions.Notifications;
using Application.Abstractions.Services;
using Application.Payments.HandleWebhook;
using Application.PfaRegistrations.Onboarding.Notifications;
using Domain.Payments;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Payments;

/// <summary>
/// Reînnoirea abonamentului vine doar din <c>invoice.payment_succeeded</c>. Evenimentele de aici au
/// forma API-ului curent, în care abonamentul stă în <c>parent</c>, nu pe linia facturii.
/// </summary>
public sealed class SubscriptionRenewalWebhookTests
{
    // Mereu în viitor: handlerul ignoră un sfârșit de perioadă deja trecut și calculează din ziua de azi,
    // așa că o dată fixă făcea testul să pice imediat ce trecea.
    private static readonly DateTime PeriodEnd = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddMonths(1).AddHours(12), DateTimeKind.Utc);

    [Fact]
    public async Task RenewalInvoiceIsRecordedOnceAndMovesTheNextBillingDate()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddSubscription(db, "sub_monthly");
        IStripeService stripe = DispatchProxy.Create<IStripeService, StripeProxy>();
        ((StripeProxy)(object)stripe).Webhook = RenewalEvent("in_renewal", "sub_monthly");
        HandleStripeWebhookCommandHandler handler = Handler(db, stripe);

        (await handler.Handle(new("payload", "signature"), default)).IsSuccess.ShouldBeTrue();
        (await handler.Handle(new("payload", "signature"), default)).IsSuccess.ShouldBeTrue();

        PaymentRecord record = await db.PaymentRecords.SingleAsync();
        record.AmountBani.ShouldBe(39900);
        record.StripePaymentId.ShouldBe("in_renewal");
        record.Status.ShouldBe(PaymentStatus.Succeeded);
        subscription.NextBillingDateUtc.ShouldBe(PeriodEnd);
    }

    [Fact]
    public async Task FailedRenewalStartsRetriesKeepsAccessAndRecordsWhyTheBankRefused()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddSubscription(db, "sub_monthly");
        IStripeService stripe = DispatchProxy.Create<IStripeService, StripeProxy>();
        var proxy = (StripeProxy)(object)stripe;
        proxy.Webhook = RenewalEvent("in_failed", "sub_monthly", "invoice.payment_failed");

        DateTime before = DateTime.UtcNow;
        (await Handler(db, stripe).Handle(new("payload", "signature"), default)).IsSuccess.ShouldBeTrue();

        // Accesul rămâne cât reîncercăm: statusul nu mai cade pe loc în „PastDue”.
        subscription.Status.ShouldBe(SubscriptionStatus.Active);
        SubscriptionPaymentRetry.IsRetrying(subscription).ShouldBeTrue();
        subscription.PaymentRetryInvoiceId.ShouldBe("in_failed");
        subscription.PaymentRetryInvoiceUrl.ShouldBe("https://invoice.stripe.com/i/in_failed");
        subscription.NextPaymentRetryAtUtc!.Value.ShouldBeInRange(before + SubscriptionPaymentRetry.Interval, DateTime.UtcNow + SubscriptionPaymentRetry.Interval);
        subscription.LastPaymentFailureReason.ShouldBe("Fonduri insuficiente (insufficient_funds)");
        proxy.StoppedAutoRetry.ShouldBe(["in_failed"]);

        PaymentRecord record = await db.PaymentRecords.SingleAsync();
        record.Status.ShouldBe(PaymentStatus.Failed);
        record.FailureCode.ShouldBe("insufficient_funds");
        record.FailureReason.ShouldBe("Fonduri insuficiente (insufficient_funds)");
        record.StripeInvoiceId.ShouldBe("in_failed");
        (await db.Notifications.SingleAsync()).Text.ShouldStartWith("Plata abonamentului nu a trecut: Fonduri insuficiente");
    }

    [Fact]
    public async Task ARepeatedEventIsRecordedOnceAndALaterAttemptDoesNotRestartTheRetries()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddSubscription(db, "sub_monthly");
        IStripeService stripe = DispatchProxy.Create<IStripeService, StripeProxy>();
        var proxy = (StripeProxy)(object)stripe;
        HandleStripeWebhookCommandHandler handler = Handler(db, stripe);

        proxy.Webhook = RenewalEvent("in_failed", "sub_monthly", "invoice.payment_failed");
        await handler.Handle(new("payload", "signature"), default);
        await handler.Handle(new("payload", "signature"), default);
        DateTime scheduled = subscription.NextPaymentRetryAtUtc!.Value;
        subscription.PaymentRetryAttempts = 1;

        // A doua încercare (a noastră), refuzată altfel: rând nou, programarea rămâne a jobului.
        proxy.DeclineCode = "do_not_honor";
        proxy.Webhook = RenewalEvent("in_failed", "sub_monthly", "invoice.payment_failed", attemptCount: 2);
        await handler.Handle(new("payload", "signature"), default);

        (await db.PaymentRecords.CountAsync()).ShouldBe(2);
        subscription.NextPaymentRetryAtUtc.ShouldBe(scheduled);
        subscription.PaymentRetryAttempts.ShouldBe(1);
        subscription.LastPaymentFailureReason.ShouldBe("Refuzat de bancă (do_not_honor)");
        proxy.StoppedAutoRetry.Count.ShouldBe(1);
    }

    [Fact]
    public async Task APaidInvoiceEndsTheRetries()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddSubscription(db, "sub_monthly");
        SubscriptionPaymentRetry.Start(subscription, "in_renewal", "https://invoice.stripe.com/i/in_renewal", DateTime.UtcNow, "Fonduri insuficiente");
        IStripeService stripe = DispatchProxy.Create<IStripeService, StripeProxy>();
        ((StripeProxy)(object)stripe).Webhook = RenewalEvent("in_renewal", "sub_monthly");

        (await Handler(db, stripe).Handle(new("payload", "signature"), default)).IsSuccess.ShouldBeTrue();

        SubscriptionPaymentRetry.IsRetrying(subscription).ShouldBeFalse();
        subscription.PaymentRetryInvoiceId.ShouldBeNull();
        subscription.Status.ShouldBe(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task PlanChangeCancelsTheOldStripeSubscription()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddSubscription(db, "sub_weekly_old");
        IStripeService stripe = DispatchProxy.Create<IStripeService, StripeProxy>();
        ((StripeProxy)(object)stripe).Webhook = new Stripe.Event
        {
            Type = "checkout.session.completed",
            Data = new Stripe.EventData
            {
                Object = new Stripe.Checkout.Session
                {
                    Id = "cs_plan_change", Mode = "subscription", PaymentStatus = "paid",
                    SubscriptionId = "sub_monthly_new", AmountTotal = 39900,
                    Metadata = new() { ["userId"] = subscription.UserId.ToString(), ["customMetadata"] = "plan:start|cycle:Monthly" },
                },
            },
        };

        (await Handler(db, stripe).Handle(new("payload", "signature"), default)).IsSuccess.ShouldBeTrue();

        subscription.StripeSubscriptionId.ShouldBe("sub_monthly_new");
        ((StripeProxy)(object)stripe).Cancelled.ShouldBe(["sub_weekly_old"]);
    }

    [Fact]
    public async Task UnpaidCheckoutIsNotRecordedAsAPayment()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddSubscription(db, "sub_monthly");
        IStripeService stripe = DispatchProxy.Create<IStripeService, StripeProxy>();
        ((StripeProxy)(object)stripe).Webhook = AdvanceCheckout("cs_unpaid", subscription.UserId, "unpaid");

        (await Handler(db, stripe).Handle(new("payload", "signature"), default)).IsSuccess.ShouldBeTrue();

        (await db.PaymentRecords.AnyAsync()).ShouldBeFalse();
        (await Application.Payments.InfiintarePaymentCheck.HasPaidAsync(db, subscription.UserId, default)).ShouldBeFalse();
    }

    [Fact]
    public async Task PaidCheckoutIsRecordedOnceEvenWhenStripeRepeatsTheEvent()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddSubscription(db, "sub_monthly");
        IStripeService stripe = DispatchProxy.Create<IStripeService, StripeProxy>();
        HandleStripeWebhookCommandHandler handler = Handler(db, stripe);

        // `completed` și apoi `async_payment_succeeded`, pe aceeași sesiune.
        ((StripeProxy)(object)stripe).Webhook = AdvanceCheckout("cs_paid", subscription.UserId, "paid");
        (await handler.Handle(new("payload", "signature"), default)).IsSuccess.ShouldBeTrue();
        ((StripeProxy)(object)stripe).Webhook = AdvanceCheckout("cs_paid", subscription.UserId, "paid", "checkout.session.async_payment_succeeded");
        (await handler.Handle(new("payload", "signature"), default)).IsSuccess.ShouldBeTrue();

        PaymentRecord record = await db.PaymentRecords.SingleAsync();
        record.Status.ShouldBe(PaymentStatus.Succeeded);
        record.StripeSessionId.ShouldBe("cs_paid");
        (await Application.Payments.InfiintarePaymentCheck.HasPaidAsync(db, subscription.UserId, default)).ShouldBeTrue();
    }

    /// <summary>Checkoutul avansului din onboarding, în starea de plată cerută.</summary>
    private static Stripe.Event AdvanceCheckout(
        string sessionId,
        Guid userId,
        string paymentStatus,
        string type = "checkout.session.completed") => new()
    {
        Id = "evt_" + sessionId,
        Type = type,
        Data = new Stripe.EventData
        {
            Object = new Stripe.Checkout.Session
            {
                Id = sessionId, Mode = "payment", Status = "complete", PaymentStatus = paymentStatus, AmountTotal = 39900,
                Metadata = new() { ["userId"] = userId.ToString(), ["customMetadata"] = "infiintare_pfa" },
            },
        },
    };

    private static Stripe.Event RenewalEvent(string invoiceId, string subscriptionId, string type = "invoice.payment_succeeded", int attemptCount = 1) =>
        Stripe.EventUtility.ParseEvent(
            $$"""
            {
              "id": "evt_{{invoiceId}}",
              "object": "event",
              "api_version": "2026-04-22.dahlia",
              "type": "{{type}}",
              "data": {
                "object": {
                  "id": "{{invoiceId}}",
                  "object": "invoice",
                  "billing_reason": "subscription_cycle",
                  "attempt_count": {{attemptCount}},
                  "hosted_invoice_url": "https://invoice.stripe.com/i/{{invoiceId}}",
                  "amount_paid": {{(type == "invoice.payment_succeeded" ? 39900 : 0)}},
                  "amount_due": 39900,
                  "parent": {
                    "type": "subscription_details",
                    "subscription_details": { "subscription": "{{subscriptionId}}" }
                  },
                  "lines": {
                    "object": "list",
                    "data": [{
                      "id": "il_1",
                      "object": "line_item",
                      "amount": 39900,
                      "period": { "start": 1790434800, "end": {{new DateTimeOffset(PeriodEnd).ToUnixTimeSeconds()}} },
                      "parent": {
                        "type": "subscription_item_details",
                        "subscription_item_details": { "subscription": "{{subscriptionId}}", "subscription_item": "si_1" }
                      }
                    }]
                  }
                }
              }
            }
            """,
            throwOnApiVersionMismatch: false);

    private static async Task<UserSubscription> AddSubscription(ApplicationDbContext db, string stripeSubscriptionId)
    {
        var user = new User { Id = Guid.NewGuid(), Email = "driver@example.test", FirstName = "Test", LastName = "Driver" };
        var subscription = new UserSubscription
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Plan = SubscriptionPlan.PfaFull,
            Status = SubscriptionStatus.Active,
            BillingCycle = SubscriptionBillingCycle.Monthly,
            StripeSubscriptionId = stripeSubscriptionId,
            FirstBillingDateUtc = PeriodEnd.AddMonths(-2),
            NextBillingDateUtc = PeriodEnd.AddMonths(-1),
        };
        db.Users.Add(user);
        db.UserSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription;
    }

    private static HandleStripeWebhookCommandHandler Handler(ApplicationDbContext db, IStripeService stripe)
    {
        IEmailService email = DispatchProxy.Create<IEmailService, NoOpProxy>();
        IMjmlRenderer renderer = DispatchProxy.Create<IMjmlRenderer, NoOpProxy>();
        IConfiguration config = new ConfigurationBuilder().Build();
        return new HandleStripeWebhookCommandHandler(db, stripe, email, renderer,
            DispatchProxy.Create<IWebPushService, NoOpProxy>(), DispatchProxy.Create<IInvoiceGenerator, NoOpProxy>(),
            new OnboardingOpsNotifier(email, renderer, config, NullLogger<OnboardingOpsNotifier>.Instance),
            null!, null!, config, NullLogger<HandleStripeWebhookCommandHandler>.Instance);
    }

    private static ApplicationDbContext Database() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Events());

    private sealed class Events : Infrastructure.DomainEvents.IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public class StripeProxy : DispatchProxy
    {
        public Stripe.Event? Webhook { get; set; }
        public List<string> Cancelled { get; } = [];
        public List<string> StoppedAutoRetry { get; } = [];
        public string DeclineCode { get; set; } = "insufficient_funds";

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IStripeService.ConstructWebhookEvent):
                    return Webhook;
                case nameof(IStripeService.CancelSubscriptionAsync):
                    Cancelled.Add((string)args![0]!);
                    return Task.CompletedTask;
                case nameof(IStripeService.GetInvoicePaymentFailureAsync):
                    return Task.FromResult(new InvoicePaymentFailure("card_declined", DeclineCode, "Your card was declined.", null));
                case nameof(IStripeService.StopInvoiceAutoRetryAsync):
                    StoppedAutoRetry.Add((string)args![0]!);
                    return Task.CompletedTask;
                default:
                    throw new InvalidOperationException("Unexpected Stripe call: " + targetMethod?.Name);
            }
        }
    }

    public class NoOpProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.ReturnType == typeof(Task<Result>))
            {
                return Task.FromResult(Result.Success());
            }
            if (targetMethod?.ReturnType == typeof(string))
            {
                return string.Empty;
            }
            return Task.CompletedTask;
        }
    }
}
