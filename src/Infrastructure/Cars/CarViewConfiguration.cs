using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Cars;

internal sealed class CarViewConfiguration : IEntityTypeConfiguration<CarView>
{
    public void Configure(EntityTypeBuilder<CarView> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.VisitorHash).HasMaxLength(64).IsRequired();
        builder.Property(v => v.Source).HasMaxLength(32).IsRequired();

        // „Câte vizualizări în ultimele 7 zile”, per mașină.
        builder.HasIndex(v => new { v.CarId, v.CreatedAtUtc });

        // Deduplicarea la scriere: același vizitator, aceeași mașină, ultimele 30 de minute.
        builder.HasIndex(v => new { v.CarId, v.VisitorHash, v.CreatedAtUtc });

        builder.HasOne(v => v.Car)
            .WithMany()
            .HasForeignKey(v => v.CarId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
