using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Banking;

internal sealed class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.HasKey(bc => bc.Id);

        builder.HasIndex(bc => bc.Reference).IsUnique();
        builder.HasIndex(bc => bc.UserId);

        builder.Property(bc => bc.Provider).HasMaxLength(32).IsRequired();
        builder.Property(bc => bc.InstitutionId).HasMaxLength(128).IsRequired();
        builder.Property(bc => bc.InstitutionName).HasMaxLength(256).IsRequired();
        builder.Property(bc => bc.InstitutionLogoUrl).HasMaxLength(512);
        builder.Property(bc => bc.ProviderRequisitionId).HasMaxLength(512).IsRequired();
        builder.Property(bc => bc.ProviderAgreementId).HasMaxLength(512);
        builder.Property(bc => bc.Reference).HasMaxLength(64).IsRequired();
        builder.Property(bc => bc.ErrorMessage).HasMaxLength(1024);

        builder.HasOne(bc => bc.User)
            .WithMany()
            .HasForeignKey(bc => bc.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
