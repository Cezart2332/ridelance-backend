using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Payments;

internal sealed class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.HasKey(s => s.Id);

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.StripeSubscriptionId).IsUnique().HasFilter("stripe_subscription_id IS NOT NULL");
        builder.HasIndex(s => s.StripeCustomerId);

        builder.Property(s => s.Plan).HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(s => s.StripeSubscriptionId).HasMaxLength(128);
        builder.Property(s => s.StripeCustomerId).HasMaxLength(128);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
