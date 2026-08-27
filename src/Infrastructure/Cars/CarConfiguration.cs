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
        builder.Property(c => c.Slug).HasMaxLength(160).IsRequired();
        builder.Property(c => c.Engine).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Transmission).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Location).HasMaxLength(512).IsRequired();
        builder.Property(c => c.PricePerWeek).HasColumnType("decimal(10,2)");
        builder.Property(c => c.OldPrice).HasColumnType("decimal(10,2)");
        builder.Property(c => c.Garantie).HasColumnType("decimal(10,2)");
        builder.Property(c => c.Description).HasMaxLength(2048);
        builder.Property(c => c.Zone).HasMaxLength(128);
        builder.Property(c => c.Color).HasMaxLength(64);
        builder.Property(c => c.MinimumPeriod).HasMaxLength(64);
        builder.Property(c => c.Conditions).HasMaxLength(1024);
        builder.Property(c => c.PlateNumber).HasMaxLength(16);
        builder.Property(c => c.Vin).HasMaxLength(32);
        builder.Property(c => c.OfferType).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.ListingStatus).HasConversion<string>().HasMaxLength(32);
        // `Active` e derivat din `ListingStatus`, deci nu are coloană.
        builder.Ignore(c => c.Active);
        builder.Property(c => c.ListingSource).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.ApprovalStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.PaymentStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(c => c.StripeCheckoutSessionId).HasMaxLength(128);
        builder.Property(c => c.StripeSubscriptionId).HasMaxLength(128);
        builder.Property(c => c.PostedByUserId);

        builder.HasIndex(c => c.Slug).IsUnique();

        // Indexul sortării „Recomandate" (spec §7.1). Ordinea coloanelor urmează exact ordinea
        // din `ORDER BY`, altfel Postgres l-ar folosi doar pentru filtrare, nu și pentru sortare.
        builder
            .HasIndex(c => new { c.ListingStatus, c.ApprovalStatus, c.PaymentStatus, c.Status, c.RecommendationScore, c.UpdatedAtUtc, c.Id })
            // Scorul și data descrescător, restul crescător — aceeași ordine ca în migrație.
            .IsDescending(false, false, false, false, true, true, false)
            .HasDatabaseName("ix_cars_recommended");
        builder.HasIndex(c => c.StripeCheckoutSessionId);
        builder.HasIndex(c => c.StripeSubscriptionId);

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
