using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Payments;

internal sealed class PaymentRecordConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
    public void Configure(EntityTypeBuilder<PaymentRecord> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => p.StripePaymentId);
        builder.HasIndex(p => p.PfaRegistrationId);

        builder.Property(p => p.PaymentType).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Description).HasMaxLength(512);
        builder.Property(p => p.StripePaymentId).HasMaxLength(128);
        builder.Property(p => p.StripeSessionId).HasMaxLength(128);

        // Fără navigație inversă: PaymentRecord e un jurnal, nu o colecție a dosarului.
        builder.HasOne<Domain.PfaRegistrations.PfaRegistration>()
            .WithMany()
            .HasForeignKey(p => p.PfaRegistrationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
