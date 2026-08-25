using Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Invoicing;

internal sealed class OblioIntegrationConfiguration : IEntityTypeConfiguration<OblioIntegration>
{
    public void Configure(EntityTypeBuilder<OblioIntegration> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.ClientId).HasMaxLength(256).IsRequired();
        // Criptat, deci mai lung decât secretul brut.
        builder.Property(o => o.ClientSecretEncrypted).HasMaxLength(1024).IsRequired();
        builder.Property(o => o.Cif).HasMaxLength(32).IsRequired();
        builder.Property(o => o.SeriesName).HasMaxLength(32);
        builder.Property(o => o.CompanyName).HasMaxLength(256);
        builder.Property(o => o.ErrorMessage).HasMaxLength(1024);

        // Seriile într-o singură coloană, separate prin virgulă. Numele lor sunt identificatori
        // scurți fără virgule („RMS", „FCT"), deci separatorul nu are ce să spargă, iar o coloană
        // text se citește la fel de ușor din psql ca din cod.
        builder.Property(o => o.AvailableSeries)
            .HasMaxLength(1024)
            .HasConversion(
                series => string.Join(',', series),
                raw => raw.Length == 0
                    ? new List<string>()
                    : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                new ValueComparer<List<string>>(
                    (left, right) => left!.SequenceEqual(right!),
                    list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
                    list => list.ToList()));

        // Un cont are cel mult o integrare Oblio.
        builder.HasIndex(o => o.UserId).IsUnique();
    }
}
