using Domain.FiscalEstimates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.FiscalProfiles;

internal sealed class FiscalEstimateRunConfiguration : IEntityTypeConfiguration<FiscalEstimateRun>
{
    public void Configure(EntityTypeBuilder<FiscalEstimateRun> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.PfaRegistrationId, r.TaxYear, r.CreatedAtUtc });
        builder.Property(r => r.RuleVersion).HasMaxLength(32);
        builder.Property(r => r.Status).HasMaxLength(32);
        builder.Property(r => r.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.AssumptionsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.MissingInputsJson).HasColumnType("jsonb").IsRequired();

        builder.HasMany(r => r.Calculations)
            .WithOne(c => c.Run)
            .HasForeignKey(c => c.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FiscalCalculationConfiguration : IEntityTypeConfiguration<FiscalCalculation>
{
    public void Configure(EntityTypeBuilder<FiscalCalculation> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Component).HasMaxLength(32);
        builder.Property(c => c.Status).HasMaxLength(32);
        builder.Property(c => c.ReasonCode).HasMaxLength(64);
        builder.Property(c => c.Amount).HasPrecision(18, 2);
        builder.Property(c => c.MissingInputsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.BreakdownJson).HasColumnType("jsonb").IsRequired();
    }
}

internal sealed class PfaPriorPeriodMonthConfiguration : IEntityTypeConfiguration<PfaPriorPeriodMonth>
{
    public void Configure(EntityTypeBuilder<PfaPriorPeriodMonth> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.PfaRegistrationId, m.Year, m.Month }).IsUnique();
        builder.Property(m => m.Income).HasPrecision(18, 2);
        builder.Property(m => m.Expenses).HasPrecision(18, 2);
    }
}
