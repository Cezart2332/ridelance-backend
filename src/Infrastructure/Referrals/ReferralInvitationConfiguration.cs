using Domain.Referrals;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Referrals;

internal sealed class ReferralInvitationConfiguration : IEntityTypeConfiguration<ReferralInvitation>
{
    public void Configure(EntityTypeBuilder<ReferralInvitation> builder)
    {
        builder.HasKey(i => i.Id);

        // O invitație pe adresă pentru fiecare client; a doua trimitere o actualizează.
        builder.HasIndex(i => new { i.InviterUserId, i.Email }).IsUnique();
        builder.HasIndex(i => i.Email);

        builder.Property(i => i.Email).HasMaxLength(256);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.InviterUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
