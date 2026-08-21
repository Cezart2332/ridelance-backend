using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Rentals;

internal sealed class RentalConfiguration : IEntityTypeConfiguration<Rental>
{
    public void Configure(EntityTypeBuilder<Rental> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantName).HasMaxLength(256).IsRequired();
        builder.Property(r => r.TenantType).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.TenantFiscalCode).HasMaxLength(32);
        builder.Property(r => r.TenantPhone).HasMaxLength(32);
        builder.Property(r => r.TenantEmail).HasMaxLength(256);
        builder.Property(r => r.FuelRule).HasMaxLength(128);
        builder.Property(r => r.Accessories).HasMaxLength(1024);
        builder.Property(r => r.Notes).HasMaxLength(2048);

        builder.HasIndex(r => new { r.OwnerUserId, r.StartAtUtc }).IsDescending(false, true);
        builder.HasIndex(r => r.CarId);
    }
}
