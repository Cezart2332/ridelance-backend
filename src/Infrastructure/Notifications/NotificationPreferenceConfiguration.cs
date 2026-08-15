using Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Notifications;

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(48).IsRequired();

        // O singură preferință per utilizator și categorie — altfel „oprit" și „pornit" ar
        // putea coexista pentru același lucru.
        builder.HasIndex(p => new { p.UserId, p.Category }).IsUnique();

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
