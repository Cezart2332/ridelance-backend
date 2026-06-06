using Domain.Bolt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Bolt;

internal sealed class BoltOrderConfiguration : IEntityTypeConfiguration<BoltOrder>
{
    public void Configure(EntityTypeBuilder<BoltOrder> builder)
    {
        builder.HasKey(bo => bo.Id);

        builder.HasIndex(bo => new { bo.UserId, bo.OrderReference }).IsUnique();

        builder.Property(bo => bo.OrderReference).HasMaxLength(128).IsRequired();
        builder.Property(bo => bo.DriverName).HasMaxLength(256).IsRequired();
        builder.Property(bo => bo.DriverUuid).HasMaxLength(128).IsRequired();
        builder.Property(bo => bo.DriverPhone).HasMaxLength(64);
        builder.Property(bo => bo.PaymentMethod).HasMaxLength(64).IsRequired();
        builder.Property(bo => bo.OrderStatus).HasMaxLength(64).IsRequired();
        builder.Property(bo => bo.PickupAddress).HasMaxLength(512).IsRequired();
        builder.Property(bo => bo.DestinationAddress).HasMaxLength(512).IsRequired();
        builder.Property(bo => bo.RideDistance).IsRequired();
        builder.Property(bo => bo.VehicleModel).HasMaxLength(128).IsRequired();
        builder.Property(bo => bo.VehicleLicensePlate).HasMaxLength(64).IsRequired();

        // Decimal precision for monetary values
        builder.Property(bo => bo.RidePrice).HasColumnType("decimal(18,2)");
        builder.Property(bo => bo.NetEarnings).HasColumnType("decimal(18,2)");
        builder.Property(bo => bo.Tip).HasColumnType("decimal(18,2)");
        builder.Property(bo => bo.Commission).HasColumnType("decimal(18,2)");

        builder.HasOne(bo => bo.User)
            .WithMany()
            .HasForeignKey(bo => bo.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
