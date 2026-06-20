using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class PfaFiscalProfileConfiguration : IEntityTypeConfiguration<PfaFiscalProfile>
{
    public void Configure(EntityTypeBuilder<PfaFiscalProfile> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasIndex(p => p.PfaRegistrationId).IsUnique();

        builder.Property(p => p.TaxationSystem).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.AccountingRegime).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.SpecialVatCodeStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.UberStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.BoltStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.OtherPlatformsStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.CashRevenueStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.CashRegisterStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.VehicleUsageType).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.VehicleSupportingDocumentLabel).HasMaxLength(256);

        builder.HasOne(p => p.PfaRegistration)
            .WithOne(r => r.FiscalProfile)
            .HasForeignKey<PfaFiscalProfile>(p => p.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.SpecialVatCodeDocument)
            .WithMany()
            .HasForeignKey(p => p.SpecialVatCodeDocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.VehicleSupportingDocument)
            .WithMany()
            .HasForeignKey(p => p.VehicleSupportingDocumentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
