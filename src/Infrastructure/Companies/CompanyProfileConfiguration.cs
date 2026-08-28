using Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Companies;

internal sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.OwnerType).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.LegalName).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Cui).HasMaxLength(32);
        builder.Property(c => c.Iban).HasMaxLength(34);
        builder.Property(c => c.RegCom).HasMaxLength(64);
        builder.Property(c => c.LegalRepresentative).HasMaxLength(256);
        builder.Property(c => c.RegisteredOffice).HasMaxLength(512);
        builder.Property(c => c.Phone).HasMaxLength(32);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Website).HasMaxLength(256);
        builder.Property(c => c.PublicDescription).HasMaxLength(2048);
        builder.Property(c => c.LogoUrl).HasMaxLength(512);
        builder.Property(c => c.Slug).HasMaxLength(160).IsRequired();

        // Un cont are cel mult un profil, iar slug-ul e identitatea publică: ambele unice.
        builder.HasIndex(c => c.UserId).IsUnique();
        builder.HasIndex(c => c.Slug).IsUnique();
    }
}
