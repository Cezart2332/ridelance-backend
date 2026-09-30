using Domain.Uber;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Uber;

internal sealed class UberTripConfiguration : IEntityTypeConfiguration<UberTrip>
{
    public void Configure(EntityTypeBuilder<UberTrip> builder)
    {
        builder.HasKey(t => t.Id);

        builder.HasIndex(t => new { t.PfaRegistrationId, t.TripUuid }).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.RequestedAtUtc });

        builder.Property(t => t.TripUuid).HasMaxLength(64);
        builder.Property(t => t.PickupAddress).HasMaxLength(512);
        builder.Property(t => t.DestinationAddress).HasMaxLength(512);
        builder.Property(t => t.Status).HasMaxLength(32);
        builder.Property(t => t.ProductType).HasMaxLength(64);
        builder.Property(t => t.PaymentType).HasMaxLength(32);

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.PfaRegistration)
            .WithMany()
            .HasForeignKey(t => t.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.UberCsvImport)
            .WithMany()
            .HasForeignKey(t => t.UberCsvImportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
