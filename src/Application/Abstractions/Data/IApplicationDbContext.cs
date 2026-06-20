using Domain.Bolt;
using Domain.Cars;
using Domain.Chat;
using Domain.Documents;
using Domain.Expenses;
using Domain.Notifications;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<PfaRegistration> PfaRegistrations { get; }
    DbSet<PfaFiscalProfile> PfaFiscalProfiles { get; }
    DbSet<PfaPlatformAccount> PfaPlatformAccounts { get; }
    DbSet<PfaFleetConsent> PfaFleetConsents { get; }
    DbSet<PfaMonthlyIncome> PfaMonthlyIncomes { get; }
    DbSet<PfaInternalNote> PfaInternalNotes { get; }
    DbSet<PfaActivityLog> PfaActivityLogs { get; }
    DbSet<Document> Documents { get; }
    DbSet<DeductibleExpense> DeductibleExpenses { get; }
    DbSet<ChatRoom> ChatRooms { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<PushSubscription> PushSubscriptions { get; }
    DbSet<Car> Cars { get; }
    DbSet<CarImage> CarImages { get; }
    DbSet<CarLead> CarLeads { get; }
    DbSet<UserSubscription> UserSubscriptions { get; }
    DbSet<PaymentRecord> PaymentRecords { get; }
    DbSet<ServiceOrder> ServiceOrders { get; }
    DbSet<BoltIntegration> BoltIntegrations { get; }
    DbSet<BoltOrder> BoltOrders { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
