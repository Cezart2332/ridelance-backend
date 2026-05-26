using Domain.Expenses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Expenses;

internal sealed class DeductibleExpenseConfiguration : IEntityTypeConfiguration<DeductibleExpense>
{
    public void Configure(EntityTypeBuilder<DeductibleExpense> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CatalogCategory).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ItemName).HasMaxLength(500).IsRequired();
        builder.Property(e => e.DeductibleLabel).HasMaxLength(100).IsRequired();
        builder.Property(e => e.AmountRon).HasPrecision(18, 2);

        builder.HasIndex(e => new { e.PfaRegistrationId, e.Year, e.Month });

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.PfaRegistration)
            .WithMany()
            .HasForeignKey(e => e.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Document)
            .WithMany()
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
