using Application.Abstractions.Data;
using Domain.Accounting;
using Domain.AppSettings;
using Domain.Banking;
using Domain.Bolt;
using Domain.Cars;
using Domain.Companies;
using Domain.Invoicing;
using Domain.Maintenance;
using Domain.Rentals;
using Domain.Chat;
using Domain.Documents;
using Domain.Expenses;
using Domain.FiscalEstimates;
using Domain.FiscalProfiles;
using Domain.Taxes;
using Domain.Notifications;
using Domain.Office;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
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
    public DbSet<CompanyProfile> CompanyProfiles { get; set; }
    public DbSet<MaintenanceEntry> MaintenanceEntries { get; set; }
    public DbSet<Rental> Rentals { get; set; }
    public DbSet<RentalPayment> RentalPayments { get; set; }
    public DbSet<CheckRecord> CheckRecords { get; set; }
    public DbSet<CheckPhoto> CheckPhotos { get; set; }
    public DbSet<VehicleEvent> VehicleEvents { get; set; }
    public DbSet<SignatureRequest> SignatureRequests { get; set; }
    public DbSet<GeneratedDocument> GeneratedDocuments { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<FleetRentalDefaults> FleetRentalDefaults { get; set; }
    public DbSet<OblioIntegration> OblioIntegrations { get; set; }
    public DbSet<PfaRegistration> PfaRegistrations { get; set; }
    public DbSet<PfaFiscalProfile> PfaFiscalProfiles { get; set; }
    public DbSet<PfaPlatformAccount> PfaPlatformAccounts { get; set; }
    public DbSet<PfaFleetConsent> PfaFleetConsents { get; set; }
    public DbSet<PfaMonthlyIncome> PfaMonthlyIncomes { get; set; }
    public DbSet<PfaTaxProfile> PfaTaxProfiles { get; set; }
    public DbSet<PfaTaxProfileRevision> PfaTaxProfileRevisions { get; set; }
    public DbSet<PfaDataCorrectionRequest> PfaDataCorrectionRequests { get; set; }
    public DbSet<FiscalProfileReminder> FiscalProfileReminders { get; set; }
    public DbSet<AdminCallTask> AdminCallTasks { get; set; }
    public DbSet<FiscalEstimateRun> FiscalEstimateRuns { get; set; }
    public DbSet<FiscalCalculation> FiscalCalculations { get; set; }
    public DbSet<PfaPriorPeriodMonth> PfaPriorPeriodMonths { get; set; }
    public DbSet<PfaInternalNote> PfaInternalNotes { get; set; }
    public DbSet<PfaActivityLog> PfaActivityLogs { get; set; }
    public DbSet<OnboardingSectionApproval> OnboardingSectionApprovals { get; set; }
    public DbSet<OnboardingEligibilityProfile> OnboardingEligibilityProfiles { get; set; }
    public DbSet<PfaPartnerLead> PfaPartnerLeads { get; set; }
    public DbSet<CompanyFormationRequest> CompanyFormationRequests { get; set; }
    public DbSet<CompanyFormationOwner> CompanyFormationOwners { get; set; }
    public DbSet<CompanyFormationConsent> CompanyFormationConsents { get; set; }
    public DbSet<CompanyFormationSignature> CompanyFormationSignatures { get; set; }
    public DbSet<ConsultoOffice> ConsultoOffices { get; set; }
    public DbSet<LegalConsentFlow> LegalConsentFlows { get; set; }
    public DbSet<LegalConsentStep> LegalConsentSteps { get; set; }
    public DbSet<OnboardingSignaturePacket> OnboardingSignaturePackets { get; set; }

    public DbSet<OnboardingStepAudit> OnboardingStepAudits { get; set; }
    public DbSet<OnboardingAnswer> OnboardingAnswers { get; set; }
    public DbSet<OnboardingSignatureDocument> OnboardingSignatureDocuments { get; set; }
    public DbSet<PfaBankAccountDeclaration> PfaBankAccountDeclarations { get; set; }
    public DbSet<PfaOblioAccount> PfaOblioAccounts { get; set; }
    public DbSet<ArrAuthorizationRequest> ArrAuthorizationRequests { get; set; }
    public DbSet<ArrAccount> ArrAccounts { get; set; }
    public DbSet<PfaVehicle> PfaVehicles { get; set; }
    public DbSet<VehicleCopyRequest> VehicleCopyRequests { get; set; }
    public DbSet<VehicleBadge> VehicleBadges { get; set; }
    public DbSet<Document> Documents { get; set; }
    public DbSet<ExtractedField> ExtractedFields { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<DeductibleExpense> DeductibleExpenses { get; set; }

    public DbSet<TaxObligation> TaxObligations { get; set; }

    // Contabilitate PFA (spec contabilitate B0).
    public DbSet<PlatformDocument> PlatformDocuments { get; set; }
    public DbSet<DocumentExtraction> DocumentExtractions { get; set; }
    public DbSet<SupplierTaxProfile> SupplierTaxProfiles { get; set; }
    public DbSet<VatRate> VatRates { get; set; }
    public DbSet<D100Rule> D100Rules { get; set; }
    public DbSet<ExchangeRate> ExchangeRates { get; set; }
    public DbSet<AnafDeclarationSchema> AnafDeclarationSchemas { get; set; }
    public DbSet<ExpenseCategoryRule> ExpenseCategoryRules { get; set; }
    public DbSet<RetentionPolicy> RetentionPolicies { get; set; }
    public DbSet<Declaration> Declarations { get; set; }
    public DbSet<DeclarationVersion> DeclarationVersions { get; set; }
    public DbSet<DeclarationLine> DeclarationLines { get; set; }
    public DbSet<VatRegistrationRequest> VatRegistrationRequests { get; set; }
    public DbSet<AnafConnection> AnafConnections { get; set; }
    public DbSet<AnafAuthorizationRequest> AnafAuthorizationRequests { get; set; }
    public DbSet<AnafPfaLink> AnafPfaLinks { get; set; }
    public DbSet<EFacturaMessage> EFacturaMessages { get; set; }
    public DbSet<SpvAgentKey> SpvAgentKeys { get; set; }
    public DbSet<SpvSyncRun> SpvSyncRuns { get; set; }
    public DbSet<SpvMessage> SpvMessages { get; set; }
    public DbSet<SpvRequest> SpvRequests { get; set; }
    public DbSet<PfaAccountingSetting> PfaAccountingSettings { get; set; }
    public DbSet<CashRegisterState> CashRegisterStates { get; set; }
    public DbSet<PfaAccountingEngagement> PfaAccountingEngagements { get; set; }
    public DbSet<PfaAccountingPeriod> PfaAccountingPeriods { get; set; }
    public DbSet<PeriodCorrection> PeriodCorrections { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<BackgroundJob> BackgroundJobs { get; set; }
    public DbSet<LedgerEntry> LedgerEntries { get; set; }
    public DbSet<ExpenseDocument> ExpenseDocuments { get; set; }
    public DbSet<ZReport> ZReports { get; set; }
    public DbSet<PfaAsset> PfaAssets { get; set; }
    public DbSet<PfaMonthCheck> PfaMonthChecks { get; set; }

    public DbSet<NotificationPreference> NotificationPreferences { get; set; }
    public DbSet<ChatRoom> ChatRooms { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<PushSubscription> PushSubscriptions { get; set; }
    public DbSet<Car> Cars { get; set; }
    public DbSet<CarImage> CarImages { get; set; }
    public DbSet<CarLead> CarLeads { get; set; }
    public DbSet<CarView> CarViews { get; set; }
    public DbSet<CarFavorite> CarFavorites { get; set; }
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
    public DbSet<UberTrip> UberTrips { get; set; }
    public DbSet<Domain.Eldrive.EldriveInvite> EldriveInvites { get; set; }
    public DbSet<Domain.Accounting.LedgerMatchProposal> LedgerMatchProposals { get; set; }
    public DbSet<Domain.Accounting.FiscalReceipt> FiscalReceipts { get; set; }
    public DbSet<Domain.Accounting.AccountingPeriodSnapshot> AccountingPeriodSnapshots { get; set; }
    public DbSet<Domain.Accounting.FixedAssetRule> FixedAssetRules { get; set; }
    public DbSet<Domain.Accounting.TaxRule> TaxRules { get; set; }
    public DbSet<Domain.Accounting.NonResidentPayment> NonResidentPayments { get; set; }
    public DbSet<Domain.Accounting.DeclarationRectificationTask> DeclarationRectificationTasks { get; set; }
    public DbSet<Domain.Accounting.NonResidentTaxDecision> NonResidentTaxDecisions { get; set; }
    public DbSet<Domain.Accounting.RentalContract> RentalContracts { get; set; }
    public DbSet<Domain.Accounting.RentPayment> RentPayments { get; set; }
    public DbSet<Domain.Accounting.AnnualTaxAnswers> AnnualTaxAnswers { get; set; }
    public DbSet<Domain.Accounting.DepreciationLine> DepreciationLines { get; set; }
    public DbSet<Domain.Accounting.InventoryCount> InventoryCounts { get; set; }
    public DbSet<Domain.Accounting.InventoryItem> InventoryItems { get; set; }
    public DbSet<Domain.Accounting.AccountingYear> AccountingYears { get; set; }
    public DbSet<Domain.Accounting.ReconciliationExplanation> ReconciliationExplanations { get; set; }
    public DbSet<Domain.FiscalLink.FiscalLinkClient> FiscalLinkClients { get; set; }
    public DbSet<OfficeAppointment> OfficeAppointments { get; set; }
    public DbSet<OfficeScheduleDay> OfficeScheduleDays { get; set; }
    public DbSet<OfficeBlockedSlot> OfficeBlockedSlots { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        modelBuilder.HasDefaultSchema(Schemas.Default);
    }

    /// <summary>
    /// Salvarea sincronă trece prin aceleași verificări (ștergeri, invarianți, Tax Engine): aplicația
    /// folosește doar varianta asincronă, dar nicio cale nu le poate ocoli.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        RejectAccountingDeletes();
        await EnsureLedgerInvariantsAsync(cancellationToken);
        List<IDomainEvent> domainEvents = ExtractDomainEvents();
        int result = await base.SaveChangesAsync(cancellationToken);

        await PublishDomainEventsAsync(domainEvents);

        return result;
    }

    /// <summary>
    /// Înregistrările fiscale și contabile nu se șterg fizic (spec contabilitate §0 pct. 7): orice
    /// ștergere ajunsă aici e o greșeală de program, nu o operațiune permisă.
    /// </summary>
    private void RejectAccountingDeletes()
    {
        string? deleted = ChangeTracker
            .Entries()
            .Where(entry => entry.State == EntityState.Deleted && entry.Entity is IAccountingRecord)
            .Select(entry => entry.Entity.GetType().Name)
            .FirstOrDefault();

        if (deleted is not null)
        {
            throw new InvalidOperationException($"{deleted} e o înregistrare contabilă și nu se poate șterge fizic.");
        }
    }

    /// <summary>
    /// Invarianții ledger-ului (spec flux contabil §4) pe înregistrările adăugate sau modificate: fiecare
    /// în parte, apoi împărțirea tranzacțiilor bancare de care se leagă.
    /// </summary>
    private async Task EnsureLedgerInvariantsAsync(CancellationToken cancellationToken)
    {
        List<Domain.Accounting.LedgerEntry> changed = [.. ChangeTracker
            .Entries<Domain.Accounting.LedgerEntry>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity)];
        if (changed.Count == 0)
        {
            return;
        }

        changed.ForEach(e => e.RefreshTaxableIncome());
        changed.ForEach(Domain.Accounting.LedgerInvariants.EnsureValid);
        RejectLockedChanges();

        List<Guid> transactionIds = [.. changed.Where(e => e.BankTransactionId is not null).Select(e => e.BankTransactionId!.Value).Distinct()];
        if (transactionIds.Count == 0)
        {
            return;
        }

        Dictionary<Guid, decimal> amounts = await BankTransactions.AsNoTracking()
            .Where(t => transactionIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Amount, cancellationToken);

        // Starea după salvare: ce e în bază, înlocuit cu ce e urmărit acum (modificat sau nou).
        var tracked = ChangeTracker.Entries<Domain.Accounting.LedgerEntry>()
            .Where(entry => entry.State != EntityState.Deleted)
            .ToDictionary(entry => entry.Entity.Id, entry => entry.Entity);
        List<Domain.Accounting.LedgerEntry> stored = await LedgerEntries.AsNoTracking()
            .Where(e => e.BankTransactionId != null && transactionIds.Contains(e.BankTransactionId.Value))
            .ToListAsync(cancellationToken);
        IEnumerable<Domain.Accounting.LedgerEntry> after = stored
            .Where(e => !tracked.ContainsKey(e.Id))
            .Concat(tracked.Values.Where(e => e.BankTransactionId is { } id && transactionIds.Contains(id)));

        foreach (IGrouping<(Guid Pfa, Guid Transaction), Domain.Accounting.LedgerEntry> links in after.GroupBy(e => (e.PfaRegistrationId, e.BankTransactionId!.Value)))
        {
            if (!amounts.TryGetValue(links.Key.Transaction, out decimal amount))
            {
                continue;
            }

            IReadOnlyList<string> violations = Domain.Accounting.LedgerInvariants.CheckBankLinks(amount, [.. links]);
            if (violations.Count > 0)
            {
                throw new Domain.Accounting.LedgerInvariantException(links.First().Id, violations);
            }
        }
    }

    /// <summary>
    /// O înregistrare blocată nu se modifică (spec flux contabil §4); se corectează prin stornare în
    /// luna curentă. Singura schimbare permisă e starea, la redeschiderea lunii.
    /// </summary>
    private void RejectLockedChanges()
    {
        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<Domain.Accounting.LedgerEntry> entry in ChangeTracker.Entries<Domain.Accounting.LedgerEntry>()
                     .Where(e => e.State == EntityState.Modified &&
                                 e.OriginalValues.GetValue<Domain.Accounting.LedgerEntryStatus>(nameof(Domain.Accounting.LedgerEntry.Status)) == Domain.Accounting.LedgerEntryStatus.Locked))
        {
            List<string> changed = [.. entry.Properties
                .Where(p => p.IsModified && p.Metadata.Name != nameof(Domain.Accounting.LedgerEntry.Status) && !Equals(p.OriginalValue, p.CurrentValue))
                .Select(p => p.Metadata.Name)];
            if (changed.Count > 0)
            {
                throw new Domain.Accounting.LedgerInvariantException(
                    entry.Entity.Id, [$"Înregistrarea e blocată; se corectează prin stornare ({string.Join(", ", changed)})."]);
            }
        }
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
