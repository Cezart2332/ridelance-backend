using Domain.Accounting;
using Domain.Banking;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Accounting;

// Maparea modulului de contabilitate PFA (spec contabilitate B0).
//
// Reguli comune:
// - enum-urile se stochează ca text;
// - sumele au 2 zecimale, cotele 4, cursurile 6;
// - JSON-ul e `jsonb`;
// - toate cheile străine sunt `Restrict`: nicio ștergere nu cascadează peste date fiscale sau
//   contabile, iar fișierele sursă nu pot dispărea de sub documentele care le folosesc.

internal static class AccountingMapping
{
    public const int PeriodLength = 7;
    public const int EnumLength = 48;

    public static PropertyBuilder<TEnum> AsText<TEnum>(this PropertyBuilder<TEnum> property) =>
        property.HasConversion<string>().HasMaxLength(EnumLength);

    public static PropertyBuilder<TValue> AsJson<TValue>(this PropertyBuilder<TValue> property) =>
        property.HasColumnType("jsonb");

    public static PropertyBuilder<decimal> AsMoney(this PropertyBuilder<decimal> property) => property.HasPrecision(18, 2);

    public static PropertyBuilder<decimal?> AsMoney(this PropertyBuilder<decimal?> property) => property.HasPrecision(18, 2);

    public static PropertyBuilder<decimal> AsRate(this PropertyBuilder<decimal> property) => property.HasPrecision(9, 4);

    public static PropertyBuilder<decimal?> AsRate(this PropertyBuilder<decimal?> property) => property.HasPrecision(9, 4);

    public static void RestrictToUser<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, object?>> key)
        where T : class =>
        builder.HasOne<User>().WithMany().HasForeignKey(key).OnDelete(DeleteBehavior.Restrict);

    public static void RestrictToDocument<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, object?>> key)
        where T : class =>
        builder.HasOne<Document>().WithMany().HasForeignKey(key).OnDelete(DeleteBehavior.Restrict);

    public static void RestrictToPfa<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, object?>> key)
        where T : class =>
        builder.HasOne<PfaRegistration>().WithMany().HasForeignKey(key).OnDelete(DeleteBehavior.Restrict);
}

internal sealed class PlatformDocumentConfiguration : IEntityTypeConfiguration<PlatformDocument>
{
    public void Configure(EntityTypeBuilder<PlatformDocument> builder)
    {
        builder.ToTable("platform_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Period).HasMaxLength(AccountingMapping.PeriodLength).IsRequired();
        builder.Property(d => d.Platform).HasConversion<string>().HasMaxLength(AccountingMapping.EnumLength);
        builder.Property(d => d.DocumentType).AsText();
        builder.Property(d => d.Status).AsText();
        builder.Property(d => d.FileHash).HasMaxLength(64).IsRequired();
        builder.Property(d => d.ExtractionError).HasMaxLength(1000);
        builder.Property(d => d.PdfText).HasColumnType("text");

        builder.HasIndex(d => new { d.PfaRegistrationId, d.Period });
        // Același fișier la același PFA e duplicat (B1: 409 cu link spre cel existent).
        builder.HasIndex(d => new { d.PfaRegistrationId, d.FileHash }).IsUnique();

        builder.HasOne(d => d.PfaRegistration).WithMany().HasForeignKey(d => d.PfaRegistrationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.SourceDocument).WithMany().HasForeignKey(d => d.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.RestrictToUser(d => d.UploadedByUserId);
        builder.RestrictToUser(d => d.ReviewedByUserId);
    }
}

internal sealed class DocumentExtractionConfiguration : IEntityTypeConfiguration<DocumentExtraction>
{
    public void Configure(EntityTypeBuilder<DocumentExtraction> builder)
    {
        builder.ToTable("document_extractions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.SupplierName).HasMaxLength(256);
        builder.Property(e => e.SupplierCountry).HasMaxLength(2);
        builder.Property(e => e.SupplierVatId).HasMaxLength(32);
        builder.Property(e => e.InvoiceNumber).HasMaxLength(64);
        builder.Property(e => e.Currency).HasMaxLength(3);
        builder.Property(e => e.Amount).AsMoney();
        builder.Property(e => e.CommissionAmount).AsMoney();
        builder.Property(e => e.OtherAmountsJson).AsJson();
        builder.Property(e => e.SourceSnippetsJson).AsJson();
        builder.Property(e => e.ChecksResultJson).AsJson();
        builder.Property(e => e.ManuallyEditedFieldsJson).AsJson();
        builder.Property(e => e.ModelId).HasMaxLength(128);
        builder.Property(e => e.PromptVersion).HasMaxLength(64);
        builder.Property(e => e.EditReason).HasMaxLength(1000);

        builder.HasIndex(e => new { e.PlatformDocumentId, e.Version }).IsUnique();
        // O singură extracție curentă pe document.
        builder.HasIndex(e => e.PlatformDocumentId).IsUnique().HasFilter("is_current");
        // `NOT_DUPLICATE` caută după furnizor + număr.
        builder.HasIndex(e => new { e.SupplierVatId, e.InvoiceNumber });

        builder.HasOne(e => e.PlatformDocument).WithMany(d => d.Extractions).HasForeignKey(e => e.PlatformDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.RestrictToUser(e => e.CreatedByUserId);
    }
}

internal sealed class SupplierTaxProfileConfiguration : IEntityTypeConfiguration<SupplierTaxProfile>
{
    public void Configure(EntityTypeBuilder<SupplierTaxProfile> builder)
    {
        builder.ToTable("supplier_tax_profiles");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SupplierName).HasMaxLength(256).IsRequired();
        builder.Property(s => s.Country).HasMaxLength(2).IsRequired();
        builder.Property(s => s.VatId).HasMaxLength(32).IsRequired();
        builder.Property(s => s.IncomeType).HasMaxLength(32).IsRequired();
        builder.Property(s => s.Treaty).HasMaxLength(128);
        builder.Property(s => s.D100Rate).AsRate();
        builder.Property(s => s.Note).HasMaxLength(500);
        builder.HasIndex(s => new { s.VatId, s.ValidFrom });
        builder.RestrictToDocument(s => s.ResidenceCertDocumentId);
    }
}

internal sealed class VatRateConfiguration : IEntityTypeConfiguration<VatRate>
{
    public void Configure(EntityTypeBuilder<VatRate> builder)
    {
        builder.ToTable("vat_rates");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Rate).AsRate();
        builder.HasIndex(v => v.ValidFrom).IsUnique();
    }
}

internal sealed class D100RuleConfiguration : IEntityTypeConfiguration<D100Rule>
{
    public void Configure(EntityTypeBuilder<D100Rule> builder)
    {
        builder.ToTable("d100_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Code).AsText();
        builder.Property(r => r.Description).HasMaxLength(500).IsRequired();
        builder.Property(r => r.ParametersJson).AsJson();
        builder.HasIndex(r => new { r.Code, r.ValidFrom }).IsUnique();
    }
}

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("exchange_rates");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Rate).HasPrecision(18, 6);
        builder.Property(r => r.Source).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => new { r.Currency, r.Date, r.Source }).IsUnique();
    }
}

internal sealed class AnafDeclarationSchemaConfiguration : IEntityTypeConfiguration<AnafDeclarationSchema>
{
    public void Configure(EntityTypeBuilder<AnafDeclarationSchema> builder)
    {
        builder.ToTable("anaf_declaration_schemas");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.DeclarationType).AsText();
        builder.Property(s => s.Version).HasMaxLength(64).IsRequired();
        builder.Property(s => s.XsdPath).HasMaxLength(256);
        builder.Property(s => s.ValidatorVersion).HasMaxLength(64);
        builder.HasIndex(s => new { s.DeclarationType, s.ValidFrom }).IsUnique();
    }
}

internal sealed class ExpenseCategoryRuleConfiguration : IEntityTypeConfiguration<ExpenseCategoryRule>
{
    public void Configure(EntityTypeBuilder<ExpenseCategoryRule> builder)
    {
        builder.ToTable("expense_category_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Category).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Label).HasMaxLength(128).IsRequired();
        builder.Property(r => r.DefaultDeductibility).AsText();
        builder.Property(r => r.CounterpartyPattern).HasMaxLength(256);
        builder.HasIndex(r => new { r.Category, r.ValidFrom }).IsUnique();
    }
}

internal sealed class RetentionPolicyConfiguration : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> builder)
    {
        builder.ToTable("retention_policies");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.ValidFrom).IsUnique();
    }
}

internal sealed class DeclarationConfiguration : IEntityTypeConfiguration<Declaration>
{
    public void Configure(EntityTypeBuilder<Declaration> builder)
    {
        builder.ToTable("declarations");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Period).HasMaxLength(AccountingMapping.PeriodLength).IsRequired();
        builder.Property(d => d.Type).AsText();
        // Idempotența generării (B3): o singură declarație de un tip pe PFA și lună.
        builder.HasIndex(d => new { d.PfaRegistrationId, d.Period, d.Type }).IsUnique();
        builder.HasOne(d => d.PfaRegistration).WithMany().HasForeignKey(d => d.PfaRegistrationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DeclarationVersionConfiguration : IEntityTypeConfiguration<DeclarationVersion>
{
    public void Configure(EntityTypeBuilder<DeclarationVersion> builder)
    {
        builder.ToTable("declaration_versions");
        builder.HasKey(v => v.Id);

        // Concurența optimistă pe `xmin`, ca la `User`: tranzițiile de status nu se calcă una pe alta.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(v => v.Kind).AsText();
        builder.Property(v => v.Status).AsText();
        builder.Property(v => v.Amount).AsMoney();
        builder.Property(v => v.SnapshotJson).AsJson();
        builder.Property(v => v.ValidationResultJson).AsJson();
        builder.Property(v => v.StatusHistoryJson).AsJson();
        builder.Property(v => v.ReceiptNumber).HasMaxLength(64);
        builder.Property(v => v.RectificationReason).HasMaxLength(1000);

        builder.HasIndex(v => new { v.DeclarationId, v.VersionNo }).IsUnique();
        builder.HasIndex(v => v.Status);

        builder.HasOne(v => v.Declaration).WithMany(d => d.Versions).HasForeignKey(v => v.DeclarationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(v => v.Schema).WithMany().HasForeignKey(v => v.SchemaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(v => v.XmlDocument).WithMany().HasForeignKey(v => v.XmlDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(v => v.PdfDocument).WithMany().HasForeignKey(v => v.PdfDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(v => v.ReceiptDocument).WithMany().HasForeignKey(v => v.ReceiptDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.RestrictToUser(v => v.CreatedByUserId);
    }
}

internal sealed class DeclarationLineConfiguration : IEntityTypeConfiguration<DeclarationLine>
{
    public void Configure(EntityTypeBuilder<DeclarationLine> builder)
    {
        builder.ToTable("declaration_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.RuleCode).HasMaxLength(64).IsRequired();
        builder.Property(l => l.Base).AsMoney();
        builder.Property(l => l.Rate).AsRate();
        builder.Property(l => l.Value).AsMoney();
        builder.Property(l => l.Currency).HasMaxLength(3).IsRequired();
        builder.Property(l => l.ExchangeRate).HasPrecision(18, 6);
        builder.Property(l => l.Explanation).HasMaxLength(500).IsRequired();
        builder.Property(l => l.SupplierName).HasMaxLength(256).IsRequired();
        builder.Property(l => l.SupplierCountry).HasMaxLength(2).IsRequired();
        builder.Property(l => l.SupplierVatId).HasMaxLength(32).IsRequired();
        builder.Property(l => l.OperationType).HasMaxLength(2);
        builder.Property(l => l.Treaty).HasMaxLength(128);

        // `includedIn` pe document: toate versiunile care îl referă.
        builder.HasIndex(l => l.SourceDocumentId);

        builder.HasOne(l => l.DeclarationVersion).WithMany(v => v.Lines).HasForeignKey(l => l.DeclarationVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.SourceDocument).WithMany().HasForeignKey(l => l.SourceDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PfaAccountingSettingConfiguration : IEntityTypeConfiguration<PfaAccountingSetting>
{
    public void Configure(EntityTypeBuilder<PfaAccountingSetting> builder)
    {
        builder.ToTable("pfa_accounting_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Key).HasMaxLength(64).IsRequired();
        builder.Property(s => s.ValueJson).AsJson();
        builder.Property(s => s.Note).HasMaxLength(1000).IsRequired();
        // Append-only: aceeași cheie nu are două valori de la aceeași dată.
        builder.HasIndex(s => new { s.PfaRegistrationId, s.Key, s.ValidFrom }).IsUnique();
        builder.HasOne(s => s.PfaRegistration).WithMany().HasForeignKey(s => s.PfaRegistrationId).OnDelete(DeleteBehavior.Restrict);
        builder.RestrictToUser(s => s.ChangedByUserId);
    }
}

internal sealed class CashRegisterStateConfiguration : IEntityTypeConfiguration<CashRegisterState>
{
    public void Configure(EntityTypeBuilder<CashRegisterState> builder)
    {
        builder.ToTable("cash_register_states");
        builder.HasKey(c => c.PfaRegistrationId);
        builder.Property(c => c.Status).AsText();
        builder.HasOne(c => c.PfaRegistration).WithOne().HasForeignKey<CashRegisterState>(c => c.PfaRegistrationId).OnDelete(DeleteBehavior.Restrict);
        builder.RestrictToUser(c => c.VerifiedByUserId);
        builder.RestrictToDocument(c => c.EvidenceDocumentId);
    }
}

internal sealed class PfaAccountingEngagementConfiguration : IEntityTypeConfiguration<PfaAccountingEngagement>
{
    public void Configure(EntityTypeBuilder<PfaAccountingEngagement> builder)
    {
        builder.ToTable("pfa_accounting_engagements");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Status).AsText();
        builder.HasIndex(e => new { e.PfaRegistrationId, e.StartDate });
        builder.HasOne(e => e.PfaRegistration).WithMany().HasForeignKey(e => e.PfaRegistrationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PfaAccountingPeriodConfiguration : IEntityTypeConfiguration<PfaAccountingPeriod>
{
    public void Configure(EntityTypeBuilder<PfaAccountingPeriod> builder)
    {
        builder.ToTable("pfa_accounting_periods");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Period).HasMaxLength(AccountingMapping.PeriodLength).IsRequired();
        builder.Property(p => p.Status).AsText();
        builder.HasIndex(p => new { p.PfaRegistrationId, p.Period }).IsUnique();
        builder.HasOne(p => p.PfaRegistration).WithMany().HasForeignKey(p => p.PfaRegistrationId).OnDelete(DeleteBehavior.Restrict);
        builder.RestrictToUser(p => p.ClosedByUserId);
    }
}

internal sealed class PeriodCorrectionConfiguration : IEntityTypeConfiguration<PeriodCorrection>
{
    public void Configure(EntityTypeBuilder<PeriodCorrection> builder)
    {
        builder.ToTable("period_corrections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Period).HasMaxLength(AccountingMapping.PeriodLength).IsRequired();
        builder.Property(c => c.ChangeJson).AsJson();
        builder.Property(c => c.Reason).HasMaxLength(1000).IsRequired();
        builder.HasIndex(c => new { c.PfaRegistrationId, c.Period });
        builder.RestrictToPfa(c => c.PfaRegistrationId);
        builder.HasOne<LedgerEntry>().WithMany().HasForeignKey(c => c.LedgerEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.RestrictToUser(c => c.CreatedByUserId);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Entity).HasMaxLength(64).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.BeforeJson).AsJson();
        builder.Property(a => a.AfterJson).AsJson();
        builder.Property(a => a.Reason).HasMaxLength(1000);
        // Tabul „Istoric” al dosarului și auditul unei entități.
        builder.HasIndex(a => new { a.PfaRegistrationId, a.AtUtc });
        builder.HasIndex(a => new { a.Entity, a.EntityId });
        builder.RestrictToPfa(a => a.PfaRegistrationId);
        builder.RestrictToUser(a => a.UserId);
    }
}

internal sealed class BackgroundJobConfiguration : IEntityTypeConfiguration<BackgroundJob>
{
    public void Configure(EntityTypeBuilder<BackgroundJob> builder)
    {
        builder.ToTable("background_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Type).AsText();
        builder.Property(j => j.Status).AsText();
        builder.Property(j => j.ParametersJson).AsJson();
        builder.Property(j => j.ResultJson).AsJson();
        builder.HasIndex(j => new { j.Status, j.CreatedAtUtc });
        builder.RestrictToDocument(j => j.FileDocumentId);
        builder.RestrictToUser(j => j.CreatedByUserId);
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");
        builder.HasKey(e => e.Id);
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(e => e.DocumentLabel).HasMaxLength(128).IsRequired();
        builder.Property(e => e.Source).AsText();
        builder.Property(e => e.ExternalId).HasMaxLength(128);
        builder.Property(e => e.Counterparty).HasMaxLength(256);
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.TransactionType).AsText();
        builder.Property(e => e.PaymentMethod).AsText();
        builder.Property(e => e.Amount).AsMoney();
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Category).HasMaxLength(64);
        builder.Property(e => e.DeductibilityType).HasConversion<string>().HasMaxLength(AccountingMapping.EnumLength);
        builder.Property(e => e.DeductiblePercent).HasPrecision(5, 2);
        builder.Property(e => e.DeductibleAmount).AsMoney();
        builder.Property(e => e.Status).AsText();
        builder.Property(e => e.AccountingPeriod).HasMaxLength(AccountingMapping.PeriodLength).IsRequired();

        builder.HasIndex(e => new { e.PfaRegistrationId, e.Date });
        builder.HasIndex(e => new { e.PfaRegistrationId, e.AccountingPeriod });
        // Importatorii sunt idempotenți prin (sursă, id extern) — B6.
        builder.HasIndex(e => new { e.Source, e.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL");

        builder.HasOne(e => e.PfaRegistration).WithMany().HasForeignKey(e => e.PfaRegistrationId).OnDelete(DeleteBehavior.Restrict);
        builder.RestrictToDocument(e => e.SourceDocumentId);
        builder.HasOne<PlatformDocument>().WithMany().HasForeignKey(e => e.PlatformDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BankTransaction>().WithMany().HasForeignKey(e => e.BankTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PfaAccountingSetting>().WithMany().HasForeignKey(e => e.DeductibilitySettingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ExpenseCategoryRule>().WithMany().HasForeignKey(e => e.DeductibilityRuleId).OnDelete(DeleteBehavior.Restrict);
        builder.RestrictToUser(e => e.CreatedByUserId);
    }
}

internal sealed class ExpenseDocumentConfiguration : IEntityTypeConfiguration<ExpenseDocument>
{
    public void Configure(EntityTypeBuilder<ExpenseDocument> builder)
    {
        builder.ToTable("expense_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Merchant).HasMaxLength(256);
        builder.Property(d => d.MerchantCui).HasMaxLength(16);
        builder.Property(d => d.Total).AsMoney();
        builder.Property(d => d.ItemsJson).AsJson();
        builder.HasIndex(d => d.PfaRegistrationId);
        builder.RestrictToPfa(d => d.PfaRegistrationId);
        builder.RestrictToDocument(d => d.DocumentId);
        builder.HasOne<LedgerEntry>().WithMany().HasForeignKey(d => d.LedgerEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.RestrictToUser(d => d.UploadedByUserId);
    }
}

internal sealed class ZReportConfiguration : IEntityTypeConfiguration<ZReport>
{
    public void Configure(EntityTypeBuilder<ZReport> builder)
    {
        builder.ToTable("z_reports");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.ZNumber).HasMaxLength(32).IsRequired();
        builder.Property(z => z.Total).AsMoney();
        builder.HasIndex(z => new { z.PfaRegistrationId, z.ZNumber }).IsUnique();
        builder.RestrictToPfa(z => z.PfaRegistrationId);
        builder.RestrictToDocument(z => z.DocumentId);
        builder.HasOne<LedgerEntry>().WithMany().HasForeignKey(z => z.LedgerEntryId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PfaAssetConfiguration : IEntityTypeConfiguration<PfaAsset>
{
    public void Configure(EntityTypeBuilder<PfaAsset> builder)
    {
        builder.ToTable("pfa_assets");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Type).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(500).IsRequired();
        builder.Property(a => a.AcquisitionValue).AsMoney();
        builder.Property(a => a.Status).AsText();
        builder.HasIndex(a => a.PfaRegistrationId);
        builder.RestrictToPfa(a => a.PfaRegistrationId);
        builder.RestrictToDocument(a => a.DocumentId);
    }
}
