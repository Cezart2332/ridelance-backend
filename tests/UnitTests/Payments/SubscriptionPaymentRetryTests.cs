using System.Reflection;
using Application.Abstractions.Services;
using Application.Payments;
using Domain.Payments;
using Domain.Users;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Payments;

/// <summary>
/// Reîncercările de încasare: de 5 ori, la 3 zile, la aceeași oră; după a cincea refuzată,
/// abonamentul se suspendă și se oprește la Stripe.
/// </summary>
public sealed class SubscriptionPaymentRetryTests
{
    private static readonly DateTime FailedAt = new(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_declined_retry_is_rescheduled_three_days_later_at_the_same_hour()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddRetrying(db);
        IStripeService stripe = Stripe(new InvoiceRetryResult(InvoiceRetryOutcome.Declined, "card_declined", "insufficient_funds"));

        // Jobul rulează la 10 minute: prinde reîncercarea la 14:37, dar următoarea rămâne la 14:30.
        DateTime run = FailedAt + SubscriptionPaymentRetry.Interval + TimeSpan.FromMinutes(7);
        (await Runner(db, stripe).RunDueAsync(run, default)).ShouldBe(1);

        subscription.PaymentRetryAttempts.ShouldBe(1);
        subscription.NextPaymentRetryAtUtc.ShouldBe(FailedAt + SubscriptionPaymentRetry.Interval * 2);
        subscription.Status.ShouldBe(SubscriptionStatus.Active);
        subscription.LastPaymentFailureReason.ShouldBe("Fonduri insuficiente (insufficient_funds)");
    }

    [Fact]
    public async Task Nothing_runs_before_the_retry_is_due()
    {
        await using ApplicationDbContext db = Database();
        await AddRetrying(db);
        IStripeService stripe = Stripe(new InvoiceRetryResult(InvoiceRetryOutcome.Declined));

        (await Runner(db, stripe).RunDueAsync(FailedAt + TimeSpan.FromDays(2), default)).ShouldBe(0);
        ((RetryStripe)(object)stripe).Retried.ShouldBeEmpty();
    }

    [Fact]
    public async Task Five_declined_retries_over_fifteen_days_suspend_the_subscription()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddRetrying(db);
        IStripeService stripe = Stripe(new InvoiceRetryResult(InvoiceRetryOutcome.Declined, "card_declined", "do_not_honor"));
        SubscriptionPaymentRetryRunner runner = Runner(db, stripe);

        for (int day = 3; day <= 15; day += 3)
        {
            await runner.RunDueAsync(FailedAt.AddDays(day), default);
        }

        var proxy = (RetryStripe)(object)stripe;
        proxy.Retried.Count.ShouldBe(SubscriptionPaymentRetry.MaxAttempts);
        subscription.Status.ShouldBe(SubscriptionStatus.Expired);
        subscription.NextPaymentRetryAtUtc.ShouldBeNull();
        proxy.Cancelled.ShouldBe(["sub_retry"]);
        (await db.Notifications.SingleAsync()).Text.ShouldContain("Accesul a fost suspendat");

        // Suspendat: nu se mai încearcă nimic.
        (await runner.RunDueAsync(FailedAt.AddDays(30), default)).ShouldBe(0);
    }

    [Fact]
    public async Task A_paid_invoice_ends_the_retries_without_touching_the_status()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddRetrying(db);

        await Runner(db, Stripe(new InvoiceRetryResult(InvoiceRetryOutcome.Paid))).RunDueAsync(FailedAt.AddDays(3), default);

        SubscriptionPaymentRetry.IsRetrying(subscription).ShouldBeFalse();
        subscription.Status.ShouldBe(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task A_stripe_outage_does_not_use_up_an_attempt()
    {
        await using ApplicationDbContext db = Database();
        UserSubscription subscription = await AddRetrying(db);
        IStripeService stripe = Stripe(null);

        DateTime run = FailedAt.AddDays(3);
        await Runner(db, stripe).RunDueAsync(run, default);

        subscription.PaymentRetryAttempts.ShouldBe(0);
        subscription.NextPaymentRetryAtUtc.ShouldBe(run + SubscriptionPaymentRetryRunner.TechnicalErrorDelay);
    }

    [Theory]
    [InlineData("card_declined", "insufficient_funds", null, "Fonduri insuficiente (insufficient_funds)")]
    [InlineData("expired_card", null, "Your card has expired.", "Card expirat (expired_card)")]
    [InlineData("authentication_required", null, null, "Banca cere confirmarea plății (3D Secure) (authentication_required)")]
    [InlineData("card_declined", "something_new", "Your card was declined.", "Your card was declined. (something_new)")]
    [InlineData(null, null, null, "Motiv necunoscut")]
    public void Decline_codes_read_as_reasons(string? code, string? declineCode, string? message, string expected) =>
        PaymentFailureReasons.Describe(code, declineCode, message).ShouldBe(expected);

    private static async Task<UserSubscription> AddRetrying(ApplicationDbContext db)
    {
        var user = new User { Id = Guid.NewGuid(), Email = "driver@example.test", FirstName = "Test", LastName = "Driver" };
        var subscription = new UserSubscription
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Plan = SubscriptionPlan.PfaFull,
            Status = SubscriptionStatus.Active,
            StripeSubscriptionId = "sub_retry",
            FirstBillingDateUtc = FailedAt.AddMonths(-1),
        };
        SubscriptionPaymentRetry.Start(subscription, "in_retry", "https://invoice.stripe.com/i/in_retry", FailedAt, "Fonduri insuficiente");
        db.Users.Add(user);
        db.UserSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription;
    }

    private static IStripeService Stripe(InvoiceRetryResult? result)
    {
        IStripeService stripe = DispatchProxy.Create<IStripeService, RetryStripe>();
        ((RetryStripe)(object)stripe).Result = result;
        return stripe;
    }

    private static SubscriptionPaymentRetryRunner Runner(ApplicationDbContext db, IStripeService stripe) =>
        new(db, stripe, NullLogger<SubscriptionPaymentRetryRunner>.Instance);

    private static ApplicationDbContext Database() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Events());

    private sealed class Events : Infrastructure.DomainEvents.IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Stripe fals. Fără rezultat setat, reîncercarea aruncă: Stripe căzut.</summary>
    public class RetryStripe : DispatchProxy
    {
        public InvoiceRetryResult? Result { get; set; }
        public List<string> Retried { get; } = [];
        public List<string> Cancelled { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IStripeService.RetryInvoicePaymentAsync):
                    Retried.Add((string)args![0]!);
                    return Result is null
                        ? Task.FromException<InvoiceRetryResult>(new global::Stripe.StripeException("Stripe indisponibil"))
                        : Task.FromResult(Result);
                case nameof(IStripeService.CancelSubscriptionAsync):
                    Cancelled.Add((string)args![0]!);
                    return Task.CompletedTask;
                default:
                    throw new InvalidOperationException("Unexpected Stripe call: " + targetMethod?.Name);
            }
        }
    }
}
