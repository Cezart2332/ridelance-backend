using Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Notifications;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.Id);

        builder.HasIndex(n => n.UserId);

        builder.Property(n => n.Text).HasMaxLength(1024).IsRequired();
        builder.Property(n => n.Type).HasMaxLength(32).IsRequired();
        builder.Property(n => n.DedupeKey).HasMaxLength(128);

        // Căutarea se face exact așa: „am mai trimis notificarea asta?". Filtrat, fiindcă
        // majoritatea notificărilor n-au cheie.
        builder
            .HasIndex(n => n.DedupeKey)
            .HasFilter("dedupe_key IS NOT NULL");

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
