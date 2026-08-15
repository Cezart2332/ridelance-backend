using Domain.Taxes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Taxes;

internal sealed class TaxObligationConfiguration : IEntityTypeConfiguration<TaxObligation>
{
    public void Configure(EntityTypeBuilder<TaxObligation> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(o => o.AmountDue).HasPrecision(18, 2);
        builder.Property(o => o.Note).HasMaxLength(1000);

        // Lista se citește mereu pentru un PFA, ordonată descrescător după perioadă.
        builder.HasIndex(o => new { o.PfaRegistrationId, o.PeriodYear, o.PeriodMonth });

        builder.HasOne(o => o.PfaRegistration)
            .WithMany()
            .HasForeignKey(o => o.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Ștergerea documentului nu șterge obligația: suma și termenul rămân datorate.
        builder.HasOne(o => o.Document)
            .WithMany()
            .HasForeignKey(o => o.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.CreatedByUser)
            .WithMany()
            .HasForeignKey(o => o.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
