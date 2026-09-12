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
        // Consimțământul e cheia după care se regăsește rândul la fiecare răspuns al furnizorului;
        // două rânduri cu același identificator și-ar suprascrie reciproc tokenurile.
        builder.HasIndex(bc => bc.ProviderConsentId).IsUnique();

        builder.Property(bc => bc.Provider).HasMaxLength(32).IsRequired();
        builder.Property(bc => bc.InstitutionId).HasMaxLength(128).IsRequired();
        builder.Property(bc => bc.InstitutionName).HasMaxLength(256).IsRequired();
        builder.Property(bc => bc.InstitutionLogoUrl).HasMaxLength(512);
        builder.Property(bc => bc.PsuIpAddress).HasMaxLength(45);
        builder.Property(bc => bc.ProviderConsentId).HasMaxLength(128).IsRequired();
        builder.Property(bc => bc.AccessTokenEncrypted).HasMaxLength(2048);
        builder.Property(bc => bc.RefreshTokenEncrypted).HasMaxLength(2048);
        builder.Property(bc => bc.ConsentStatus).HasMaxLength(32);
        builder.Property(bc => bc.Reference).HasMaxLength(64).IsRequired();
        builder.Property(bc => bc.ErrorMessage).HasMaxLength(1024);

        builder.HasOne(bc => bc.User)
            .WithMany()
            .HasForeignKey(bc => bc.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
