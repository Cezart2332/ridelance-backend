using Application.Abstractions.Authentication;
using Application.Abstractions.Notifications;
using Application.PfaRegistrations.ClientNotifications;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>Contabilul trimite notificări doar clienților lui PFA.</summary>
public sealed class ClientNotificationTests
{
    [Fact]
    public async Task AssignedAccountant_NotifiesClient_WithPushAndHistory()
    {
        await using ApplicationDbContext db = NewDb();
        (User contabil, User client, PfaRegistration pfa) = Seed(db);
        db.PushSubscriptions.Add(new PushSubscription { Id = Guid.NewGuid(), UserId = client.Id, Endpoint = "https://push.test/1", P256dh = "k", Auth = "a" });
        await db.SaveChangesAsync();
        var push = new RecordingPush();

        Result<SendClientNotificationResponse> result = await Handler(db, contabil.Id, push).Handle(
            new SendClientNotificationCommand(pfa.Id, "  Te rog încarcă extrasul pe septembrie.  ", ClientNotificationDestinations.RecurringDocuments),
            default);

        result.IsSuccess.ShouldBeTrue();
        Notification sent = await db.Notifications.SingleAsync();
        sent.UserId.ShouldBe(client.Id);
        sent.Type.ShouldBe(NotificationTypes.AccountantMessage);
        sent.Text.ShouldBe("Te rog încarcă extrasul pe septembrie.");
        sent.SectionKey.ShouldBe(ClientNotificationDestinations.RecurringDocuments);
        push.Bodies.ShouldBe(["Te rog încarcă extrasul pe septembrie."]);
        result.Value.PushSent.ShouldBe(1);
        (await db.PfaActivityLogs.SingleAsync()).ActivityType.ShouldBe("ClientNotified");
    }

    [Fact]
    public async Task OtherAccountant_IsRefused()
    {
        await using ApplicationDbContext db = NewDb();
        (_, _, PfaRegistration pfa) = Seed(db);
        User stranger = AddUser(db, UserRole.Contabil);
        await db.SaveChangesAsync();

        Result<SendClientNotificationResponse> result = await Handler(db, stranger.Id, new RecordingPush())
            .Handle(new SendClientNotificationCommand(pfa.Id, "Salut", null), default);

        result.IsFailure.ShouldBeTrue();
        (await db.Notifications.AnyAsync()).ShouldBeFalse();
    }

    [Theory]
    [InlineData("   ", null)]
    [InlineData("Salut", "Admin")]
    public async Task EmptyTextOrUnknownDestination_IsRejected(string text, string? destination)
    {
        await using ApplicationDbContext db = NewDb();
        (User contabil, _, PfaRegistration pfa) = Seed(db);

        Result<SendClientNotificationResponse> result = await Handler(db, contabil.Id, new RecordingPush())
            .Handle(new SendClientNotificationCommand(pfa.Id, text, destination), default);

        result.IsFailure.ShouldBeTrue();
        (await db.Notifications.AnyAsync()).ShouldBeFalse();
    }

    // ── Infrastructură de test ───────────────────────────────────────────────

    private static SendClientNotificationCommandHandler Handler(ApplicationDbContext db, Guid callerId, IWebPushService push) =>
        new(db, new StubUser(callerId), push, new ConfigurationBuilder().Build());

    private static (User Contabil, User Client, PfaRegistration Pfa) Seed(ApplicationDbContext db)
    {
        User contabil = AddUser(db, UserRole.Contabil);
        User client = AddUser(db, UserRole.Client);
        var pfa = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = client.Id,
            User = client,
            AssignedContabilId = contabil.Id,
            Cui = "12345678",
            LegalName = "POPESCU ION PFA",
            PfaSource = PfaSource.Existing,
            Status = PfaRegistrationStatus.Approved,
        };
        db.PfaRegistrations.Add(pfa);
        db.SaveChanges();
        return (contabil, client, pfa);
    }

    private static User AddUser(ApplicationDbContext db, UserRole role)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@test.ro",
            FirstName = "Test",
            LastName = role.ToString(),
            Role = role,
        };
        db.Users.Add(user);
        return user;
    }

    private static ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private sealed class RecordingPush : IWebPushService
    {
        public List<string> Bodies { get; } = [];

        public Task SendPushNotificationAsync(PushSubscription subscription, string title, string body, string? url = null, CancellationToken cancellationToken = default)
        {
            Bodies.Add(body);
            return Task.CompletedTask;
        }
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
