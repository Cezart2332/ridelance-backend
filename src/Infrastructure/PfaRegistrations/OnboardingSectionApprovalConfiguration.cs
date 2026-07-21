using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class OnboardingSectionApprovalConfiguration : IEntityTypeConfiguration<OnboardingSectionApproval>
{
    public void Configure(EntityTypeBuilder<OnboardingSectionApproval> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.PfaRegistrationId, a.SectionKey }).IsUnique();

        builder.Property(a => a.SectionKey).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Note).HasMaxLength(1024);

        builder.HasOne(a => a.PfaRegistration)
            .WithMany(r => r.OnboardingSections)
            .HasForeignKey(a => a.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.ValidatedByUser)
            .WithMany()
            .HasForeignKey(a => a.ValidatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
