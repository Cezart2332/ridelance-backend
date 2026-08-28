using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Cars;

internal sealed class VehicleEventConfiguration : IEntityTypeConfiguration<VehicleEvent>
{
    public void Configure(EntityTypeBuilder<VehicleEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Description).HasMaxLength(512).IsRequired();

        // Cronologia se citește într-un singur fel: a unei mașini, de la cel mai recent.
        builder.HasIndex(e => new { e.CarId, e.OccurredAtUtc }).IsDescending(false, true);
    }
}
