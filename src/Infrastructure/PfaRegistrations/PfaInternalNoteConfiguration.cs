using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class PfaInternalNoteConfiguration : IEntityTypeConfiguration<PfaInternalNote>
{
    public void Configure(EntityTypeBuilder<PfaInternalNote> builder)
    {
        builder.HasKey(n => n.Id);

        builder.HasOne(n => n.PfaRegistration)
            .WithMany()
            .HasForeignKey(n => n.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.CreatedByUser)
            .WithMany()
            .HasForeignKey(n => n.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
