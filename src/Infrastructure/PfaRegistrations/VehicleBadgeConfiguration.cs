using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class VehicleBadgeConfiguration : IEntityTypeConfiguration<VehicleBadge>
{
    public void Configure(EntityTypeBuilder<VehicleBadge> builder)
    {
        builder.HasKey(b => b.Id);

        builder.HasIndex(b => new { b.PfaVehicleId, b.Provider }).IsUnique();

        builder.Property(b => b.Provider).HasConversion<string>().HasMaxLength(16);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(16);
    }
}
