using Application.Abstractions.Authentication;
using Application.Admin;
using Domain.Payments;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Admin;

/// <summary>
/// Suspendarea din admin merge și pentru clientul din onboarding care n-are încă dosar PFA: lista
/// îl adresează prin id-ul contului, iar suspendarea îi taie accesul chiar fără abonament.
/// </summary>
public sealed class ClientSuspensionTests
{
    [Fact]
    public async Task A_client_without_registration_can_be_suspended_and_reactivated()
    {
        await using ApplicationDbContext db = NewDb();
        (Guid admin, Guid client) = AddUsers(db);
        await db.SaveChangesAsync();

        Result<AdminPfaDetailResponse> suspended = await new SuspendAdminPfaCommandHandler(db, new StubUser(admin))
            .Handle(new SuspendAdminPfaCommand(client, "  Acte false  "), default);

        suspended.IsSuccess.ShouldBeTrue();
        suspended.Value.AccountStatus.ShouldBe("Suspendat");
        User user = await db.Users.SingleAsync(u => u.Id == client);
        user.IsSuspended.ShouldBeTrue();
        user.SuspensionReason.ShouldBe("Acte false");
        user.RefreshToken.ShouldBeNull();

        Result<AdminPfaDetailResponse> reactivated = await new ReactivateAdminPfaCommandHandler(db, new StubUser(admin))
            .Handle(new ReactivateAdminPfaCommand(client, null), default);

        reactivated.Value.AccountStatus.ShouldBe("Cont nou");
        (await db.Users.SingleAsync(u => u.Id == client)).IsSuspended.ShouldBeFalse();
    }

    [Fact]
    public async Task Suspending_expires_the_subscription_of_a_client_who_has_one()
    {
        await using ApplicationDbContext db = NewDb();
        (Guid admin, Guid client) = AddUsers(db);
        db.UserSubscriptions.Add(new UserSubscription
        {
            Id = Guid.NewGuid(),
            UserId = client,
            Plan = SubscriptionPlan.PfaFull,
            Status = SubscriptionStatus.Active,
        });
        await db.SaveChangesAsync();

        await new SuspendAdminPfaCommandHandler(db, new StubUser(admin)).Handle(new SuspendAdminPfaCommand(client, null), default);

        (await db.UserSubscriptions.SingleAsync()).Status.ShouldBe(SubscriptionStatus.Expired);
    }

    [Fact]
    public async Task Only_an_admin_can_suspend()
    {
        await using ApplicationDbContext db = NewDb();
        (_, Guid client) = AddUsers(db);
        await db.SaveChangesAsync();

        Result<AdminPfaDetailResponse> result = await new SuspendAdminPfaCommandHandler(db, new StubUser(client))
            .Handle(new SuspendAdminPfaCommand(client, null), default);

        result.IsFailure.ShouldBeTrue();
        (await db.Users.SingleAsync(u => u.Id == client)).IsSuspended.ShouldBeFalse();
    }

    private static ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private static (Guid Admin, Guid Client) AddUsers(ApplicationDbContext db)
    {
        var admin = new User { Id = Guid.NewGuid(), Email = "admin@ridelance.ro", Role = UserRole.Admin };
        var client = new User
        {
            Id = Guid.NewGuid(),
            Email = "client@ridelance.ro",
            Role = UserRole.Client,
            RefreshToken = "refresh",
            RefreshTokenExpiryUtc = DateTime.UtcNow.AddDays(3),
        };
        db.Users.AddRange(admin, client);
        return (admin.Id, client.Id);
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
