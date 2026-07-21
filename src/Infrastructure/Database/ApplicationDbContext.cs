using Application.Abstractions.Data;
using Domain.Banking;
using Domain.Bolt;
using Domain.Cars;
using Domain.Chat;
using Domain.Documents;
using Domain.Expenses;
using Domain.Notifications;
using Domain.Office;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.Uber;
using Domain.Users;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Infrastructure.Database;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IDomainEventsDispatcher domainEventsDispatcher)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<PfaRegistration> PfaRegistrations { get; set; }
    public DbSet<PfaFiscalProfile> PfaFiscalProfiles { get; set; }
    public DbSet<PfaPlatformAccount> PfaPlatformAccounts { get; set; }
    public DbSet<PfaFleetConsent> PfaFleetConsents { get; set; }
    public DbSet<PfaMonthlyIncome> PfaMonthlyIncomes { get; set; }
    public DbSet<PfaInternalNote> PfaInternalNotes { get; set; }
    public DbSet<PfaActivityLog> PfaActivityLogs { get; set; }
    public DbSet<OnboardingSectionApproval> OnboardingSectionApprovals { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<DeductibleExpense> DeductibleExpenses { get; set; }
    public DbSet<ChatRoom> ChatRooms { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<PushSubscription> PushSubscriptions { get; set; }
    public DbSet<Car> Cars { get; set; }
    public DbSet<CarImage> CarImages { get; set; }
    public DbSet<CarLead> CarLeads { get; set; }
    public DbSet<UserSubscription> UserSubscriptions { get; set; }
    public DbSet<PaymentRecord> PaymentRecords { get; set; }
    public DbSet<ServiceOrder> ServiceOrders { get; set; }
    public DbSet<IssuedInvoice> IssuedInvoices { get; set; }
    public DbSet<BoltIntegration> BoltIntegrations { get; set; }
    public DbSet<BoltOrder> BoltOrders { get; set; }
    public DbSet<BankConnection> BankConnections { get; set; }
    public DbSet<BankAccount> BankAccounts { get; set; }
    public DbSet<BankTransaction> BankTransactions { get; set; }
    public DbSet<UberCsvImport> UberCsvImports { get; set; }
    public DbSet<OfficeAppointment> OfficeAppointments { get; set; }
    public DbSet<OfficeScheduleDay> OfficeScheduleDays { get; set; }
    public DbSet<OfficeBlockedSlot> OfficeBlockedSlots { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        modelBuilder.HasDefaultSchema(Schemas.Default);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        List<IDomainEvent> domainEvents = ExtractDomainEvents();
        int result = await base.SaveChangesAsync(cancellationToken);

        await PublishDomainEventsAsync(domainEvents);

        return result;
    }

    private async Task PublishDomainEventsAsync(IEnumerable<IDomainEvent> domainEvents)
    {
        await domainEventsDispatcher.DispatchAsync(domainEvents);
    }

    private List<IDomainEvent> ExtractDomainEvents()
    {
        var domainEvents = ChangeTracker
            .Entries<Entity>()
            .Select(entry => entry.Entity)
            .SelectMany(entity =>
            {
                List<IDomainEvent> domainEvents = entity.DomainEvents;

                entity.ClearDomainEvents();

                return domainEvents;
            })
            .ToList();
        return domainEvents;
    }
}
