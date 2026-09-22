using Domain.FiscalProfiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.FiscalProfiles;

internal sealed class PfaTaxProfileConfiguration : IEntityTypeConfiguration<PfaTaxProfile>
{
    public void Configure(EntityTypeBuilder<PfaTaxProfile> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.PfaRegistrationId, p.TaxYear }).IsUnique();

        builder.Property(p => p.Regime).HasMaxLength(16);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.AnswersJson).HasColumnType("jsonb").IsRequired();
        builder.Property(p => p.PfaRegisteredOnSource).HasMaxLength(64);

        // Revizia e și ETag-ul: două salvări din aceeași versiune nu trec amândouă.
        builder.Property(p => p.Revision).IsConcurrencyToken();

        builder.HasOne(p => p.PfaRegistration)
            .WithMany()
            .HasForeignKey(p => p.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Revisions)
            .WithOne(r => r.Profile)
            .HasForeignKey(r => r.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PfaTaxProfileRevisionConfiguration : IEntityTypeConfiguration<PfaTaxProfileRevision>
{
    public void Configure(EntityTypeBuilder<PfaTaxProfileRevision> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.ProfileId, r.Revision });
        builder.Property(r => r.ActorRole).HasMaxLength(16);
        builder.Property(r => r.ChangesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(1000);
    }
}

internal sealed class PfaDataCorrectionRequestConfiguration : IEntityTypeConfiguration<PfaDataCorrectionRequest>
{
    public void Configure(EntityTypeBuilder<PfaDataCorrectionRequest> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.PfaRegistrationId, r.State });
        builder.Property(r => r.Fields).HasMaxLength(256);
        builder.Property(r => r.Details).HasMaxLength(2000);
        builder.Property(r => r.State).HasConversion<string>().HasMaxLength(16);
    }
}

internal sealed class FiscalProfileReminderConfiguration : IEntityTypeConfiguration<FiscalProfileReminder>
{
    public void Configure(EntityTypeBuilder<FiscalProfileReminder> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.PfaRegistrationId, r.TaxYear, r.WeekIndex }).IsUnique();
    }
}

internal sealed class AdminCallTaskConfiguration : IEntityTypeConfiguration<AdminCallTask>
{
    public void Configure(EntityTypeBuilder<AdminCallTask> builder)
    {
        builder.HasKey(t => t.Id);
        builder.HasIndex(t => new { t.PfaRegistrationId, t.TaxYear, t.Reason }).IsUnique();
        builder.Property(t => t.Reason).HasMaxLength(64);
        builder.Property(t => t.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.CallOutcome).HasMaxLength(2000);
    }
}
