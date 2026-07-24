using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class PfaPartnerLeadConfiguration : IEntityTypeConfiguration<PfaPartnerLead>
{
    public void Configure(EntityTypeBuilder<PfaPartnerLead> builder)
    {
        builder.HasKey(l => l.Id);

        builder.HasIndex(l => l.PfaRegistrationId).IsUnique();

        builder.Property(l => l.Provider).HasMaxLength(64).IsRequired();
        builder.Property(l => l.Phone).HasMaxLength(32);
        builder.Property(l => l.Email).HasMaxLength(256);
        builder.Property(l => l.County).HasMaxLength(128);
        builder.Property(l => l.HousingType).HasMaxLength(128);
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(l => l.AdminNote).HasMaxLength(1024);

        // Relația 1:1 cu PfaRegistration e configurată în PfaRegistrationConfiguration.
    }
}
