using Domain.PfaRegistrations.ArrFleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class ArrFleetApplicationConfiguration : IEntityTypeConfiguration<ArrFleetApplication>
{
    public void Configure(EntityTypeBuilder<ArrFleetApplication> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => a.PfaRegistrationId).IsUnique();
        builder.HasIndex(a => a.UserId);

        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Platforms).HasConversion<int>();
        builder.Property(a => a.VehicleOwnership).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.ReopenedReason).HasMaxLength(1024);

        builder.Ignore(a => a.PlatformCount);

        builder.HasOne(a => a.PfaRegistration)
            .WithOne(r => r.ArrFleetApplication)
            .HasForeignKey<ArrFleetApplication>(a => a.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.StatusLogs)
            .WithOne(l => l.Application)
            .HasForeignKey(l => l.ArrFleetApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ArrFleetStatusLogConfiguration : IEntityTypeConfiguration<ArrFleetStatusLog>
{
    public void Configure(EntityTypeBuilder<ArrFleetStatusLog> builder)
    {
        builder.HasKey(l => l.Id);

        builder.HasIndex(l => l.ArrFleetApplicationId);

        builder.Property(l => l.FromStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(l => l.ToStatus).HasConversion<string>().HasMaxLength(32);
    }
}
