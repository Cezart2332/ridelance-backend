using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class PfaOblioAccountConfiguration : IEntityTypeConfiguration<PfaOblioAccount>
{
    public void Configure(EntityTypeBuilder<PfaOblioAccount> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => a.PfaRegistrationId).IsUnique();

        builder.Property(a => a.AccountEmail).HasMaxLength(256);
        builder.Property(a => a.ConsentTextVersion).HasMaxLength(16);
        builder.Property(a => a.IntegrationStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.AdminNote).HasMaxLength(1024);

        builder.Ignore(a => a.AllConsentsAccepted);

        builder.HasOne(a => a.PfaRegistration)
            .WithOne(r => r.OblioAccount)
            .HasForeignKey<PfaOblioAccount>(a => a.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
