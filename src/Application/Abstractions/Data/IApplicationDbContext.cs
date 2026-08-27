using Domain.AppSettings;
using Domain.Banking;
using Domain.Bolt;
using Domain.Cars;
using Domain.Companies;
using Domain.Chat;
using Domain.Documents;
using Domain.Invoicing;
using Domain.Maintenance;
using Domain.Expenses;
using Domain.Taxes;
using Domain.Notifications;
using Domain.Office;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.Rentals;
using Domain.PfaRegistrations.CompanyFormation;
using Domain.Uber;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<CompanyProfile> CompanyProfiles { get; }
    DbSet<MaintenanceEntry> MaintenanceEntries { get; }
    DbSet<Rental> Rentals { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<FleetRentalDefaults> FleetRentalDefaults { get; }
    DbSet<OblioIntegration> OblioIntegrations { get; }
    DbSet<PfaRegistration> PfaRegistrations { get; }
    DbSet<PfaFiscalProfile> PfaFiscalProfiles { get; }
    DbSet<PfaPlatformAccount> PfaPlatformAccounts { get; }
    DbSet<PfaFleetConsent> PfaFleetConsents { get; }
    DbSet<PfaMonthlyIncome> PfaMonthlyIncomes { get; }
    DbSet<PfaInternalNote> PfaInternalNotes { get; }
    DbSet<PfaActivityLog> PfaActivityLogs { get; }
    DbSet<OnboardingSectionApproval> OnboardingSectionApprovals { get; }
    DbSet<OnboardingEligibilityProfile> OnboardingEligibilityProfiles { get; }
    DbSet<PfaPartnerLead> PfaPartnerLeads { get; }
    DbSet<CompanyFormationRequest> CompanyFormationRequests { get; }
    DbSet<CompanyFormationOwner> CompanyFormationOwners { get; }
    DbSet<CompanyFormationConsent> CompanyFormationConsents { get; }
    DbSet<CompanyFormationSignature> CompanyFormationSignatures { get; }
    DbSet<ConsultoOffice> ConsultoOffices { get; }
    DbSet<LegalConsentFlow> LegalConsentFlows { get; }
    DbSet<LegalConsentStep> LegalConsentSteps { get; }
    DbSet<OnboardingSignaturePacket> OnboardingSignaturePackets { get; }

    DbSet<OnboardingStepAudit> OnboardingStepAudits { get; }
    DbSet<OnboardingSignatureDocument> OnboardingSignatureDocuments { get; }
    DbSet<PfaBankAccountDeclaration> PfaBankAccountDeclarations { get; }
    DbSet<PfaOblioAccount> PfaOblioAccounts { get; }
    DbSet<ArrAuthorizationRequest> ArrAuthorizationRequests { get; }
    DbSet<ArrAccount> ArrAccounts { get; }
    DbSet<PfaVehicle> PfaVehicles { get; }
    DbSet<VehicleCopyRequest> VehicleCopyRequests { get; }
    DbSet<VehicleBadge> VehicleBadges { get; }
    DbSet<Document> Documents { get; }
    DbSet<ExtractedField> ExtractedFields { get; }
    DbSet<AppSetting> AppSettings { get; }
    DbSet<DeductibleExpense> DeductibleExpenses { get; }

    DbSet<TaxObligation> TaxObligations { get; }

    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<ChatRoom> ChatRooms { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<PushSubscription> PushSubscriptions { get; }
    DbSet<Car> Cars { get; }
    DbSet<CarImage> CarImages { get; }
    DbSet<CarLead> CarLeads { get; }
    DbSet<CarView> CarViews { get; }
    DbSet<UserSubscription> UserSubscriptions { get; }
    DbSet<PaymentRecord> PaymentRecords { get; }
    DbSet<ServiceOrder> ServiceOrders { get; }
    DbSet<IssuedInvoice> IssuedInvoices { get; }
    DbSet<BoltIntegration> BoltIntegrations { get; }
    DbSet<BoltOrder> BoltOrders { get; }
    DbSet<BankConnection> BankConnections { get; }
    DbSet<BankAccount> BankAccounts { get; }
    DbSet<BankTransaction> BankTransactions { get; }

    DbSet<BankConnectionClaim> BankConnectionClaims { get; }
    DbSet<UberCsvImport> UberCsvImports { get; }
    DbSet<OfficeAppointment> OfficeAppointments { get; }
    DbSet<OfficeScheduleDay> OfficeScheduleDays { get; }
    DbSet<OfficeBlockedSlot> OfficeBlockedSlots { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
