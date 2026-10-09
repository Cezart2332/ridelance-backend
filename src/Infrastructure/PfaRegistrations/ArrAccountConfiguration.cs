using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class ArrAccountConfiguration : IEntityTypeConfiguration<ArrAccount>
{
    public void Configure(EntityTypeBuilder<ArrAccount> builder)
    {
        builder.HasKey(a => a.Id);

        // Un singur cont activ per județ: selectul din onboarding alege după codul de județ.
        builder.HasIndex(a => a.CountyCode).IsUnique();

        builder.Property(a => a.CountyCode).HasMaxLength(2).IsRequired();
        builder.Property(a => a.CountyName).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Treasury).HasMaxLength(128).IsRequired();
        builder.Property(a => a.FiscalCode).HasMaxLength(16).IsRequired();
        builder.Property(a => a.Iban).HasMaxLength(34).IsRequired();
    }
}
