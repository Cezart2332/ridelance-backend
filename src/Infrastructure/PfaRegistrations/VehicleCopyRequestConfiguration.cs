using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class VehicleCopyRequestConfiguration : IEntityTypeConfiguration<VehicleCopyRequest>
{
    public void Configure(EntityTypeBuilder<VehicleCopyRequest> builder)
    {
        builder.HasKey(c => c.Id);

        builder.HasIndex(c => c.PfaVehicleId).IsUnique();

        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.CopyConformaNumber).HasMaxLength(64);
        builder.Property(c => c.AdminNote).HasMaxLength(1024);
    }
}
