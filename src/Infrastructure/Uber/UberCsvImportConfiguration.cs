using Domain.Uber;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Uber;

internal sealed class UberCsvImportConfiguration : IEntityTypeConfiguration<UberCsvImport>
{
    public void Configure(EntityTypeBuilder<UberCsvImport> builder)
    {
        builder.HasKey(i => i.Id);

        builder.HasIndex(i => new { i.PfaRegistrationId, i.Year, i.Month });
        builder.HasIndex(i => new { i.PfaRegistrationId, i.Year, i.Month, i.FileType, i.FileName })
            .IsUnique();

        builder.Property(i => i.FileType).HasMaxLength(32);
        builder.Property(i => i.FileName).HasMaxLength(256);
        builder.Property(i => i.NetEarnings).HasPrecision(18, 2);
        builder.Property(i => i.GrossEarnings).HasPrecision(18, 2);
        builder.Property(i => i.CashCollected).HasPrecision(18, 2);
        builder.Property(i => i.Commission).HasPrecision(18, 2);

        builder.HasOne(i => i.User)
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.PfaRegistration)
            .WithMany()
            .HasForeignKey(i => i.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
