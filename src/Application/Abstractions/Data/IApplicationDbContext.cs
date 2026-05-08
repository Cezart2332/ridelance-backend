using Domain.Cars;
using Domain.Chat;
using Domain.Documents;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<PfaRegistration> PfaRegistrations { get; }
    DbSet<Document> Documents { get; }
    DbSet<ChatRoom> ChatRooms { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<PushSubscription> PushSubscriptions { get; }
    DbSet<Car> Cars { get; }
    DbSet<CarImage> CarImages { get; }
    DbSet<CarLead> CarLeads { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
