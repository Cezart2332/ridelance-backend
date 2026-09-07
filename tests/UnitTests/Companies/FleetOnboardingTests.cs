using Application.Abstractions.Authentication;
using Application.Abstractions;
using Application.Abstractions.Notifications;
using Application.Payments.HandleWebhook;
using Application.PfaRegistrations.Onboarding.Notifications;
using Application.Abstractions.Services;
using Application.Companies.Onboarding;
using Domain.Companies;
using Domain.Payments;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using SharedKernel;
using System.Reflection;
using Xunit;

namespace UnitTests.Companies;

public sealed class FleetOnboardingTests
{
    [Fact]
    public async Task OblioStatusUsesSavedCredentialsOfCurrentOwner()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db);
        var integration = new Domain.Invoicing.OblioIntegration
        {
            Id = Guid.NewGuid(), UserId = user.Id, ClientId = "owner@example.test",
            ClientSecretEncrypted = "protected-test-token", Cif = "RO12345678", IsConnected = true
        };
        db.OblioIntegrations.Add(integration);
        db.OblioIntegrations.Add(new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), ClientId = "another@example.test", ClientSecretEncrypted = "another-token", Cif = "9999", IsConnected = true });
        await db.SaveChangesAsync();
        var handler = new Application.Invoicing.Queries.GetOwnerInvoices.GetOblioCredentialsQueryHandler(db, new CurrentUser(user.Id));
        Result<Application.Invoicing.Queries.GetOwnerInvoices.OblioCredentialsStatus> result = await handler.Handle(new(), default);
        result.Value.Connected.ShouldBeTrue();
        result.Value.AccountEmail.ShouldBe("owner@example.test");
        result.Value.HasApiKey.ShouldBeTrue();
        var connections = new Application.Connections.Queries.GetConnections.GetConnectionsQueryHandler(db, new CurrentUser(user.Id));
        (await connections.Handle(new(), default)).Value.Single(c => c.Provider == "Oblio").Status.ShouldBe("connected");
        integration.IsConnected = false;
        await db.SaveChangesAsync();
        (await handler.Handle(new(), default)).Value.Connected.ShouldBeFalse();
        (await connections.Handle(new(), default)).Value.Single(c => c.Provider == "Oblio").Status.ShouldBe("disconnected");
    }
    [Theory]
    [InlineData("paid", true)]
    [InlineData("unpaid", false)]
    public async Task WebhookRequiresPaidStatusAndDoesNotDuplicatePayment(string paymentStatus, bool expected)
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, 7);
        IStripeService stripe = DispatchProxy.Create<IStripeService, StripeProxy>();
        ((StripeProxy)(object)stripe).Webhook = new Stripe.Event
        {
            Type = "checkout.session.completed",
            Data = new Stripe.EventData
            {
                Object = new Stripe.Checkout.Session
                {
                    Id = "cs_test_paid", Mode = "subscription", PaymentStatus = paymentStatus,
                    SubscriptionId = "sub_test", AmountTotal = 24900,
                    Metadata = new() { ["userId"] = user.Id.ToString(), ["customMetadata"] = "plan:fleet|cycle:Monthly|bcr:1", ["fleetOnboarding"] = "true", ["fleetBcrApplied"] = "true" }
                }
            }
        };
        IEmailService email = DispatchProxy.Create<IEmailService, NoOpProxy>();
        IMjmlRenderer renderer = DispatchProxy.Create<IMjmlRenderer, NoOpProxy>();
        IConfiguration config = new ConfigurationBuilder().Build();
        var handler = new HandleStripeWebhookCommandHandler(db, stripe, email, renderer,
            DispatchProxy.Create<IWebPushService, NoOpProxy>(), DispatchProxy.Create<IInvoiceGenerator, NoOpProxy>(),
            new OnboardingOpsNotifier(email, renderer, config, NullLogger<OnboardingOpsNotifier>.Instance),
            null!, config, NullLogger<HandleStripeWebhookCommandHandler>.Instance);
        (await handler.Handle(new("payload", "signature"), default)).IsSuccess.ShouldBeTrue();
        (await handler.Handle(new("payload", "signature"), default)).IsSuccess.ShouldBeTrue();
        (user.FleetOnboarding.CompletedAtUtc is not null).ShouldBe(expected);
        (await db.PaymentRecords.CountAsync()).ShouldBe(expected ? 1 : 0);
        if (expected)
        {
            UserSubscription subscription = await db.UserSubscriptions.SingleAsync();
            subscription.Plan.ShouldBe(SubscriptionPlan.Fleet);
            subscription.BcrDiscountConfirmedAtUtc.ShouldNotBeNull();
        }
    }
    [Theory]
    [InlineData(SubscriptionBillingCycle.Monthly, false, 29900)]
    [InlineData(SubscriptionBillingCycle.Monthly, true, 24900)]
    [InlineData(SubscriptionBillingCycle.Annual, false, 322920)]
    [InlineData(SubscriptionBillingCycle.Annual, true, 292920)]
    public void QuoteMatchesCatalogAndBenefit(SubscriptionBillingCycle cycle, bool eligible, long expected)
    {
        FleetPricing.AmountDue(cycle, eligible).ShouldBe(expected);
        StripeCatalogItem item = cycle == SubscriptionBillingCycle.Annual ? StripeCatalog.FleetAnnual : StripeCatalog.Fleet;
        (item.UnitAmountBani - (eligible ? FleetPricing.BcrCoupon(cycle).AmountOffBani : 0)).ShouldBe(expected);
    }

    [Fact]
    public async Task CompanyIsConfirmedFromStoredLookupAndSurvivesReload()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db);
        FleetOnboardingService service = Service(db, user);
        (await service.SaveAsync(new(1, Cui: "RO12345678"), default)).IsSuccess.ShouldBeTrue();
        user.FleetOnboarding.CompletedStep.ShouldBe(0);
        (await service.SaveAsync(new(1, ConfirmCompany: true, Cui: "99999999"), default)).IsSuccess.ShouldBeTrue();
        db.ChangeTracker.Clear();
        User reloaded = await db.Users.SingleAsync();
        reloaded.FleetOnboarding.Company!.Cui.ShouldBe("12345678");
        (await db.CompanyProfiles.SingleAsync()).LegalName.ShouldBe("Firma Test SRL");
    }

    /// <summary>
    /// Confirmarea contactelor blochează doar când e pornită din configurație.
    ///
    /// Implicit e stinsă: furnizorii de email și SMS nu sunt configurați, iar o poartă care cere
    /// un cod ce nu poate fi livrat oprea înrolarea fără nicio cale de ieșire.
    /// </summary>
    [Fact]
    public async Task UnverifiedContactsAdvanceWhileVerificationIsOff()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, 1);
        user.PhoneVerifiedAtUtc = null;

        (await Service(db, user).SaveAsync(
            new(2, FirstName: "Ion", LastName: "Pop", Position: "Administrator"), default))
            .IsSuccess.ShouldBeTrue();

        user.FleetOnboarding.CompletedStep.ShouldBe(2);
    }

    [Fact]
    public async Task UnverifiedPhoneCannotAdvanceWhenVerificationIsRequired()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, 1);
        user.PhoneVerifiedAtUtc = null;

        (await Service(db, user, requireContactVerification: true).SaveAsync(
            new(2, FirstName: "Ion", LastName: "Pop", Position: "Administrator"), default))
            .IsFailure.ShouldBeTrue();

        user.FleetOnboarding.CompletedStep.ShouldBe(1);
    }

    /// <summary>
    /// Emailul nu se mai confirmă în acest flux: e adresa contului, dovedită la înregistrare.
    /// Nici cu poarta pornită nu are voie să oprească pasul — altfel am fi cerut un al doilea cod
    /// pentru aceeași adresă.
    /// </summary>
    [Fact]
    public async Task UnverifiedEmailNeverBlocksTheFleetFlow()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, 1);
        user.EmailVerifiedAtUtc = null;

        (await Service(db, user, requireContactVerification: true).SaveAsync(
            new(2, FirstName: "Ion", LastName: "Pop", Position: "Administrator"), default))
            .IsSuccess.ShouldBeTrue();

        user.FleetOnboarding.CompletedStep.ShouldBe(2);
    }

    [Fact]
    public async Task SkippingStepsOrCheckoutIsRejected()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db);
        FleetOnboardingService service = Service(db, user);
        (await service.SaveAsync(new(6, Cycle: "Annual"), default)).IsFailure.ShouldBeTrue();
        (await service.CheckoutAsync(default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task OptionalIntegrationsCanBeDeferredButNotFalselyConnected()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, 3);
        FleetOnboardingService service = Service(db, user);
        (await service.SaveAsync(new(4), default)).IsFailure.ShouldBeTrue();
        (await service.SaveAsync(new(4, Deferred: true, BcrRequested: true), default)).IsSuccess.ShouldBeTrue();
        (await service.SaveAsync(new(5), default)).IsFailure.ShouldBeTrue();
        (await service.SaveAsync(new(5, Deferred: true), default)).IsSuccess.ShouldBeTrue();
        user.FleetOnboarding.CompletedStep.ShouldBe(5);
        user.FleetOnboarding.BcrEligibleAtUtc.ShouldBeNull();
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(12, true)]
    public async Task VehicleCountAcceptsZero(int count, bool valid)
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, 2);
        (await Service(db, user).SaveAsync(new(3, Platforms: ["Niciuna"], VehicleCount: count), default)).IsSuccess.ShouldBe(valid);
    }

    [Fact]
    public async Task NoPlatformsCannotBeCombinedWithUber()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, 2);
        (await Service(db, user).SaveAsync(new(3, Platforms: ["Niciuna", "Uber"]), default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task NewAccountNeedsBothCompletionAndActiveFleetSubscription()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, 7);
        FleetOnboardingService service = Service(db, user);
        (await service.GetAsync(default)).Value.DashboardAllowed.ShouldBeFalse();
        user.FleetOnboarding.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        (await service.GetAsync(default)).Value.DashboardAllowed.ShouldBeFalse();
        db.UserSubscriptions.Add(new() { Id = Guid.NewGuid(), UserId = user.Id, Plan = SubscriptionPlan.Fleet, Status = SubscriptionStatus.Active });
        await db.SaveChangesAsync();
        (await service.GetAsync(default)).Value.DashboardAllowed.ShouldBeTrue();
        (await db.UserSubscriptions.SingleAsync()).Status = SubscriptionStatus.PastDue;
        await db.SaveChangesAsync();
        (await service.GetAsync(default)).Value.DashboardAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task ExistingFleetKeepsAccess()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db);
        user.FleetOnboardingRequired = false;
        (await Service(db, user).GetAsync(default)).Value.DashboardAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task ClientCannotUseFleetOnboarding()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db);
        user.Role = UserRole.Client;
        await db.SaveChangesAsync();
        (await Service(db, user).GetAsync(default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task LegalAcceptanceIsRequiredAndCheckoutIsReused()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db, 6);
        IStripeService stripe = DispatchProxy.Create<IStripeService, StripeProxy>();
        FleetOnboardingService service = Service(db, user, stripe);
        (await service.SaveAsync(new(7, TermsAccepted: true), default)).IsFailure.ShouldBeTrue();
        (await service.SaveAsync(new(7, TermsAccepted: true, PrivacyAccepted: true), default)).IsSuccess.ShouldBeTrue();
        (await service.CheckoutAsync(default)).Value.ShouldBe("cs_test_123_secret_456");
        (await service.CheckoutAsync(default)).Value.ShouldBe("cs_test_123_secret_456");
        ((StripeProxy)(object)stripe).CreateCalls.ShouldBe(1);
        (await service.SaveAsync(new(6, Cycle: "Annual"), default)).IsFailure.ShouldBeTrue();
        user.FleetOnboarding.CompletedAtUtc.ShouldBeNull();
        user.FleetOnboarding.LegalVersion.ShouldBe(FleetPricing.LegalVersion);
    }

    private static ApplicationDbContext Database() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Events());

    private static async Task<User> AddUser(ApplicationDbContext db, int step = 0)
    {
        var user = new User { Id = Guid.NewGuid(), Role = UserRole.CarPoster, FleetOnboardingRequired = true,
            Email = "fleet@example.test", EmailVerifiedAtUtc = DateTime.UtcNow, PhoneVerifiedAtUtc = DateTime.UtcNow };
        user.FleetOnboarding.CompletedStep = step;
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static FleetOnboardingService Service(
        ApplicationDbContext db,
        User user,
        IStripeService? stripe = null,
        bool requireContactVerification = false) =>
        new(db, new CurrentUser(user.Id), new Lookup(), stripe ?? DispatchProxy.Create<IStripeService, StripeProxy>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://example.test",
                ["Onboarding:RequireContactVerification"] = requireContactVerification ? "true" : "false",
            }).Build());

    private sealed record CurrentUser(Guid UserId) : IUserContext;
    private sealed class Lookup : ICompanyLookupService
    {
        public Task<CompanyLookupResult?> FindByCuiAsync(string cui, CancellationToken cancellationToken = default) =>
            Task.FromResult<CompanyLookupResult?>(new(cui, "Firma Test SRL", "Strada Test", "București", "București", "J40/123/2026", false));
    }
    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    public class StripeProxy : DispatchProxy
    {
        public Stripe.Event? Webhook { get; set; }
        public int CreateCalls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IStripeService.CreateCheckoutSessionAsync))
            {
                CreateCalls++;
                return Task.FromResult("cs_test_123_secret_456");
            }
            return targetMethod?.Name switch
            {
                nameof(IStripeService.ConstructWebhookEvent) => Webhook,
                nameof(IStripeService.ResolvePriceIdAsync) => Task.FromResult("price_test"),
                nameof(IStripeService.GetSessionStatusAsync) => Task.FromResult<(string, string?)>(("open", null)),
                _ => throw new InvalidOperationException("Unexpected Stripe call: " + targetMethod?.Name),
            };
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
