using Domain.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Maintenance;

internal sealed class MaintenanceEntryConfiguration : IEntityTypeConfiguration<MaintenanceEntry>
{
    public void Configure(EntityTypeBuilder<MaintenanceEntry> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Title).HasMaxLength(256).IsRequired();
        builder.Property(m => m.Notes).HasMaxLength(2048);

        // Lista se citește aproape mereu ca „ce a avut mașina asta" sau „ce are flota mea",
        // ambele ordonate descrescător după dată.
        builder.HasIndex(m => new { m.OwnerUserId, m.PerformedAtUtc }).IsDescending(false, true);
        builder.HasIndex(m => new { m.CarId, m.PerformedAtUtc }).IsDescending(false, true);
    }
}
