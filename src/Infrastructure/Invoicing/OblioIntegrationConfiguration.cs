using Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Invoicing;

internal sealed class OblioIntegrationConfiguration : IEntityTypeConfiguration<OblioIntegration>
{
    public void Configure(EntityTypeBuilder<OblioIntegration> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.ClientId).HasMaxLength(256).IsRequired();
        // Criptat, deci mai lung decât secretul brut.
        builder.Property(o => o.ClientSecretEncrypted).HasMaxLength(1024).IsRequired();
        builder.Property(o => o.Cif).HasMaxLength(32).IsRequired();
        builder.Property(o => o.SeriesName).HasMaxLength(32);
        builder.Property(o => o.CompanyName).HasMaxLength(256);
        builder.Property(o => o.ErrorMessage).HasMaxLength(1024);

        // Un cont are cel mult o integrare Oblio.
        builder.HasIndex(o => o.UserId).IsUnique();
    }
}
