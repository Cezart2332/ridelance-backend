using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class PfaVehicleConfiguration : IEntityTypeConfiguration<PfaVehicle>
{
    public void Configure(EntityTypeBuilder<PfaVehicle> builder)
    {
        builder.HasKey(v => v.Id);

        builder.HasIndex(v => v.PfaRegistrationId);

        builder.Property(v => v.OwnershipMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(v => v.PlateNumber).HasMaxLength(32);
        builder.Property(v => v.Vin).HasMaxLength(32);
        builder.Property(v => v.Make).HasMaxLength(64);
        builder.Property(v => v.Model).HasMaxLength(64);

        builder.HasOne(v => v.PfaRegistration)
            .WithMany(r => r.Vehicles)
            .HasForeignKey(v => v.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.CopyRequest)
            .WithOne(c => c.Vehicle)
            .HasForeignKey<VehicleCopyRequest>(c => c.PfaVehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(v => v.Badges)
            .WithOne(b => b.Vehicle)
            .HasForeignKey(b => b.PfaVehicleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
