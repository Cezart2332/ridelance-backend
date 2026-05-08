using Domain.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Chat;

internal sealed class ChatRoomConfiguration : IEntityTypeConfiguration<ChatRoom>
{
    public void Configure(EntityTypeBuilder<ChatRoom> builder)
    {
        builder.HasKey(r => r.Id);

        builder.HasIndex(r => new { r.ClientUserId, r.ProfessionalUserId }).IsUnique();

        builder.HasOne(r => r.ClientUser)
            .WithMany()
            .HasForeignKey(r => r.ClientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ProfessionalUser)
            .WithMany()
            .HasForeignKey(r => r.ProfessionalUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Messages)
            .WithOne(m => m.ChatRoom)
            .HasForeignKey(m => m.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
