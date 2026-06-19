using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class PfaMonthlyIncomeConfiguration : IEntityTypeConfiguration<PfaMonthlyIncome>
{
    public void Configure(EntityTypeBuilder<PfaMonthlyIncome> builder)
    {
        builder.HasKey(i => i.Id);

        builder.HasIndex(i => new { i.PfaRegistrationId, i.Year, i.Month })
            .IsUnique();

        builder.Property(i => i.VenitCash).HasPrecision(18, 2);
        builder.Property(i => i.VenitCard).HasPrecision(18, 2);
        builder.Property(i => i.VenitBolt).HasPrecision(18, 2);
        builder.Property(i => i.VenitUber).HasPrecision(18, 2);
        builder.Property(i => i.TaxeEstimate).HasPrecision(18, 2);
        builder.Property(i => i.IsProcessed).HasDefaultValue(false);

        builder.HasOne(i => i.PfaRegistration)
            .WithMany()
            .HasForeignKey(i => i.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.ProcessedByUser)
            .WithMany()
            .HasForeignKey(i => i.ProcessedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
