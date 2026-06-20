using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class PfaFleetConsentConfiguration : IEntityTypeConfiguration<PfaFleetConsent>
{
    public void Configure(EntityTypeBuilder<PfaFleetConsent> builder)
    {
        builder.HasKey(c => c.Id);

        builder.HasIndex(c => c.PfaRegistrationId).IsUnique();

        builder.Property(c => c.ConsentTextVersion).HasMaxLength(32);

        builder.HasOne(c => c.PfaRegistration)
            .WithOne(r => r.FleetConsent)
            .HasForeignKey<PfaFleetConsent>(c => c.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
