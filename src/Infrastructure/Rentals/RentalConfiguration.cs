using System.Text.Json;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Rentals;

internal sealed class RentalConfiguration : IEntityTypeConfiguration<Rental>
{
    public void Configure(EntityTypeBuilder<Rental> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.PublicCode).HasMaxLength(16).IsRequired();
        builder.HasIndex(r => r.PublicCode).IsUnique();

        builder.Property(r => r.Lifecycle).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.FuelRule).HasMaxLength(128);
        builder.Property(r => r.FuelLevelAtPickup).HasMaxLength(32);
        builder.Property(r => r.AccessoriesOther).HasMaxLength(512);
        builder.Property(r => r.Notes).HasMaxLength(2048);

        // Aceeași formă ca listele de pe `Car`: jsonb plus comparator, ca EF să vadă modificările
        // din interiorul listei, nu doar înlocuirea ei.
        var stringListComparer = new ValueComparer<List<string>>(
            (c1, c2) => c1 != null && c2 != null && c1.SequenceEqual(c2),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList());

        builder.Property(r => r.Accessories)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(stringListComparer);

        builder
            .HasOne(r => r.Tenant)
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.OwnerUserId, r.StartAtUtc }).IsDescending(false, true);
        builder.HasIndex(r => r.CarId);
        builder.HasIndex(r => r.TenantId);
    }
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(256).IsRequired();
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Cnp).HasMaxLength(32);
        builder.Property(t => t.IdSeries).HasMaxLength(8);
        builder.Property(t => t.IdNumber).HasMaxLength(16);
        builder.Property(t => t.Cui).HasMaxLength(32);
        builder.Property(t => t.RegCom).HasMaxLength(32);
        builder.Property(t => t.Address).HasMaxLength(512);
        builder.Property(t => t.Phone).HasMaxLength(32);
        builder.Property(t => t.Email).HasMaxLength(256);
        builder.Property(t => t.DriverLicenseNumber).HasMaxLength(32);

        builder.HasIndex(t => new { t.OwnerUserId, t.Name });
    }
}

internal sealed class FleetRentalDefaultsConfiguration : IEntityTypeConfiguration<FleetRentalDefaults>
{
    public void Configure(EntityTypeBuilder<FleetRentalDefaults> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.FuelRule).HasMaxLength(128);
        builder.Property(d => d.DefaultConditions).HasMaxLength(2048);

        // Unu-la-unu cu flota: un al doilea rând ar însemna două seturi de valori implicite, iar
        // formularul n-ar avea cum să aleagă între ele.
        builder.HasIndex(d => d.OwnerUserId).IsUnique();
    }
}
