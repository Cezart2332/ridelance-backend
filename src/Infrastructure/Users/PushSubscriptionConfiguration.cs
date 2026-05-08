using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Users;

internal sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.HasKey(s => s.Id);

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.Endpoint).IsUnique();

        builder.Property(s => s.Endpoint).HasMaxLength(1024).IsRequired();
        builder.Property(s => s.P256dh).HasMaxLength(256).IsRequired();
        builder.Property(s => s.Auth).HasMaxLength(256).IsRequired();

        builder.HasOne(s => s.User)
            .WithMany(u => u.PushSubscriptions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
