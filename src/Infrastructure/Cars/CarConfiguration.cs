using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Cars;

internal sealed class CarConfiguration : IEntityTypeConfiguration<Car>
{
    public void Configure(EntityTypeBuilder<Car> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Brand).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Model).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Engine).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Transmission).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Location).HasMaxLength(512).IsRequired();
        builder.Property(c => c.PricePerWeek).HasColumnType("decimal(10,2)");
        builder.Property(c => c.OldPrice).HasColumnType("decimal(10,2)");
        builder.Property(c => c.Description).HasMaxLength(2048);
        builder.Property(c => c.OfferType).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);

        var stringListComparer = new ValueComparer<List<string>>(
            (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());

        // Store list<string> as JSON columns (PostgreSQL supports this natively)
        builder.Property(c => c.UberCategories)
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(stringListComparer);

        builder.Property(c => c.BoltCategories)
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(stringListComparer);

        builder.Property(c => c.Badges)
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(stringListComparer);


        builder.HasMany(c => c.Images)
            .WithOne(i => i.Car)
            .HasForeignKey(i => i.CarId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Leads)
            .WithOne(l => l.Car)
            .HasForeignKey(l => l.CarId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
