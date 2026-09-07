using Application.Notifications.Dismiss;
using Application.Notifications.GetNotifications;
using Application.Notifications.MarkAsRead;
using Domain.Notifications;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Notifications;

public sealed class NotificationActionsTests
{
    [Fact]
    public async Task DismissOnlyAffectsOwnerAndPreservesDeduplicationHistory()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Events());
        var owner = Guid.NewGuid();
        var item = new Notification { Id = Guid.NewGuid(), UserId = owner, DedupeKey = "monthly:2026-09", Text = "Documente" };
        db.Notifications.Add(item);
        await db.SaveChangesAsync();
        var handler = new DismissNotificationCommandHandler(db);
        await handler.Handle(new(Guid.NewGuid(), item.Id), default);
        item.IsDismissed.ShouldBeFalse();
        await handler.Handle(new(owner, item.Id), default);
        item.IsDismissed.ShouldBeTrue();
        item.IsRead.ShouldBeTrue();
        (await db.Notifications.AnyAsync(n => n.DedupeKey == "monthly:2026-09")).ShouldBeTrue();
        var query = new GetNotificationsQueryHandler(db);
        (await query.Handle(new(owner), default)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadAllKeepsOtherUsersUnreadAndReturnsRelatedContext()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Events());
        var owner = Guid.NewGuid();
        var client = Guid.NewGuid();
        var mine = new Notification { Id = Guid.NewGuid(), UserId = owner, RelatedUserId = client, SectionKey = "Pfa" };
        var other = new Notification { Id = Guid.NewGuid(), UserId = client };
        db.Notifications.AddRange(mine, other);
        await db.SaveChangesAsync();
        await new MarkAsReadCommandHandler(db).Handle(new(owner), default);
        mine.IsRead.ShouldBeTrue();
        other.IsRead.ShouldBeFalse();
        Application.Notifications.NotificationResponse response = (await new GetNotificationsQueryHandler(db).Handle(new(owner), default)).Value.Single();
        response.RelatedUserId.ShouldBe(client);
        response.SectionKey.ShouldBe("Pfa");
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
