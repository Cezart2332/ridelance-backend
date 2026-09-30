using Domain.Eldrive;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Eldrive;

internal sealed class EldriveInviteConfiguration : IEntityTypeConfiguration<EldriveInvite>
{
    public void Configure(EntityTypeBuilder<EldriveInvite> builder)
    {
        builder.HasKey(i => i.Id);

        // O singură invitație activă pe client; cele scoase rămân ca istoric.
        builder.HasIndex(i => i.UserId)
            .IsUnique()
            .HasFilter("removed_at_utc IS NULL");

        builder.Property(i => i.Email).HasMaxLength(256);
        builder.Property(i => i.Status).HasMaxLength(32);

        builder.HasOne(i => i.User)
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
