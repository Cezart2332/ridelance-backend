using Application.Abstractions.Authentication;
using Application.Abstractions.Services;
using Application.Admin.Accounts;
using Domain.Cars;
using Domain.Payments;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Admin;

/// <summary>
/// Închiderea unui cont de client din admin: accesul se oprește, datele rămân.
///
/// „Șters” nu înseamnă șters din baza de date — plățile, facturile, închirierile și documentele
/// trebuie păstrate. Se schimbă doar ce poate face contul și ce vede publicul.
/// </summary>
public sealed class ClientAccountClosingTests
{
    [Fact]
    public async Task Closing_stops_billing_keeps_the_data_and_withdraws_the_listings()
    {
        await using ApplicationDbContext db = NewDb();
        (Guid admin, Guid firm) = AddUsers(db, UserRole.CarPoster);
        db.UserSubscriptions.Add(Subscription(firm, SubscriptionStatus.Active, "sub_firm"));
        Car published = AddCar(db, firm, ListingStatus.Published);
        Car draft = AddCar(db, firm, ListingStatus.Draft);
        await db.SaveChangesAsync();
        (IStripeService stripe, StripeProxy calls) = NewStripe();

        Result<ClientAccountStatusResponse> result = await Close(db, admin, stripe).Handle(
            new CloseClientAccountCommand(firm, "  Cerere client  "), default);

        result.IsSuccess.ShouldBeTrue();
        calls.Cancelled.ShouldBe(["sub_firm"]);

        User user = await db.Users.SingleAsync(u => u.Id == firm);
        user.IsDeleted.ShouldBeTrue();
        user.DeletionReason.ShouldBe("Cerere client");
        user.DeletedByUserId.ShouldBe(admin);
        user.RefreshToken.ShouldBeNull();

        (await db.UserSubscriptions.SingleAsync()).Status.ShouldBe(SubscriptionStatus.Cancelled);
        // Mașinile rămân în baza de date — doar ies din marketplace.
        (await db.Cars.CountAsync()).ShouldBe(2);
        (await db.Cars.SingleAsync(c => c.Id == published.Id)).ListingStatus.ShouldBe(ListingStatus.Paused);
        (await db.Cars.SingleAsync(c => c.Id == draft.Id)).ListingStatus.ShouldBe(ListingStatus.Draft);
    }

    /// <summary>Dacă abonamentul nu se poate opri, contul rămâne deschis: altfel ar fi taxat închis.</summary>
    [Fact]
    public async Task A_failed_Stripe_cancellation_leaves_the_account_open()
    {
        await using ApplicationDbContext db = NewDb();
        (Guid admin, Guid client) = AddUsers(db, UserRole.Client);
        db.UserSubscriptions.Add(Subscription(client, SubscriptionStatus.Active, "sub_pfa"));
        await db.SaveChangesAsync();

        Result<ClientAccountStatusResponse> result = await Close(db, admin, NewStripe(fail: true).Service).Handle(
            new CloseClientAccountCommand(client, null), default);

        result.IsFailure.ShouldBeTrue();
        (await db.Users.SingleAsync(u => u.Id == client)).IsDeleted.ShouldBeFalse();
        (await db.UserSubscriptions.SingleAsync()).Status.ShouldBe(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Staff_accounts_cannot_be_closed_from_the_client_lists()
    {
        await using ApplicationDbContext db = NewDb();
        (Guid admin, Guid accountant) = AddUsers(db, UserRole.Contabil);
        await db.SaveChangesAsync();

        Result<ClientAccountStatusResponse> result = await Close(db, admin, NewStripe().Service).Handle(
            new CloseClientAccountCommand(accountant, null), default);

        result.Error.ShouldBe(UserErrors.CannotCloseStaffAccount);
    }

    [Fact]
    public async Task Only_an_admin_can_close_an_account()
    {
        await using ApplicationDbContext db = NewDb();
        (_, Guid client) = AddUsers(db, UserRole.Client);
        await db.SaveChangesAsync();

        Result<ClientAccountStatusResponse> result = await Close(db, client, NewStripe().Service).Handle(
            new CloseClientAccountCommand(client, null), default);

        result.IsFailure.ShouldBeTrue();
        (await db.Users.SingleAsync(u => u.Id == client)).IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Reopening_restores_access()
    {
        await using ApplicationDbContext db = NewDb();
        (Guid admin, Guid client) = AddUsers(db, UserRole.Client);
        await db.SaveChangesAsync();
        await Close(db, admin, NewStripe().Service).Handle(new CloseClientAccountCommand(client, "test"), default);

        Result<ClientAccountStatusResponse> result = await new ReopenClientAccountCommandHandler(db, new StubUser(admin))
            .Handle(new ReopenClientAccountCommand(client), default);

        result.Value.DeletedAtUtc.ShouldBeNull();
        User user = await db.Users.SingleAsync(u => u.Id == client);
        user.IsDeleted.ShouldBeFalse();
        user.DeletionReason.ShouldBeNull();
    }

    private static CloseClientAccountCommandHandler Close(ApplicationDbContext db, Guid caller, IStripeService stripe) =>
        new(db, new StubUser(caller), stripe);

    private static ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private static (Guid Admin, Guid Target) AddUsers(ApplicationDbContext db, UserRole targetRole)
    {
        var admin = new User { Id = Guid.NewGuid(), Email = "admin@ridelance.ro", Role = UserRole.Admin };
        var target = new User
        {
            Id = Guid.NewGuid(),
            Email = "client@ridelance.ro",
            Role = targetRole,
            RefreshToken = "refresh",
            RefreshTokenExpiryUtc = DateTime.UtcNow.AddDays(3),
        };
        db.Users.AddRange(admin, target);
        return (admin.Id, target.Id);
    }

    private static UserSubscription Subscription(Guid userId, SubscriptionStatus status, string stripeId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Plan = SubscriptionPlan.Fleet,
        Status = status,
        StripeSubscriptionId = stripeId,
    };

    private static Car AddCar(ApplicationDbContext db, Guid owner, ListingStatus status)
    {
        var id = Guid.NewGuid();
        var car = new Car
        {
            Id = id,
            Brand = "Dacia",
            Model = "Logan",
            Year = 2022,
            Slug = $"dacia-logan-2022-{id:N}",
            PostedByUserId = owner,
            ListingStatus = status,
            ApprovalStatus = CarApprovalStatus.Approved,
        };
        db.Cars.Add(car);
        return car;
    }

    /// <summary>Stripe fals: înregistrează abonamentele oprite; orice alt apel e o greșeală de test.</summary>
    // Public și nesigilată: DispatchProxy generează la rulare o subclasă a ei.
    public class StripeProxy : DispatchProxy
    {
        public bool Fail { get; set; }
        public List<string> Cancelled { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IStripeService.CancelSubscriptionAsync))
            {
                throw new NotSupportedException(targetMethod?.Name);
            }

            if (Fail)
            {
                return Task.FromException(new InvalidOperationException("stripe down"));
            }

            Cancelled.Add((string)args![0]!);
            return Task.CompletedTask;
        }
    }

    private static (IStripeService Service, StripeProxy Proxy) NewStripe(bool fail = false)
    {
        IStripeService service = DispatchProxy.Create<IStripeService, StripeProxy>();
        var proxy = (StripeProxy)(object)service;
        proxy.Fail = fail;
        return (service, proxy);
    }

    private sealed class StubUser(Guid id) : IUserContext
    {
        public Guid UserId { get; } = id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
