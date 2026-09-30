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
    private static readonly DateTime PeriodEnd = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

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
    public async Task FailedRenewalMarksTheSubscriptionPastDue()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddSubscription(db, "sub_monthly");
        IStripeService stripe = DispatchProxy.Create<IStripeService, StripeProxy>();
        ((StripeProxy)(object)stripe).Webhook = RenewalEvent("in_failed", "sub_monthly", "invoice.payment_failed");

        (await Handler(db, stripe).Handle(new("payload", "signature"), default)).IsSuccess.ShouldBeTrue();

        subscription.Status.ShouldBe(SubscriptionStatus.PastDue);
        (await db.PaymentRecords.SingleAsync()).Status.ShouldBe(PaymentStatus.Failed);
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

    private static Stripe.Event RenewalEvent(string invoiceId, string subscriptionId, string type = "invoice.payment_succeeded") =>
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
            Plan = SubscriptionPlan.Start,
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

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IStripeService.ConstructWebhookEvent):
                    return Webhook;
                case nameof(IStripeService.CancelSubscriptionAsync):
                    Cancelled.Add((string)args![0]!);
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
