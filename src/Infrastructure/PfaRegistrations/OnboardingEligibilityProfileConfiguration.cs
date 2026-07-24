using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class OnboardingEligibilityProfileConfiguration : IEntityTypeConfiguration<OnboardingEligibilityProfile>
{
    public void Configure(EntityTypeBuilder<OnboardingEligibilityProfile> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasIndex(p => p.UserId).IsUnique();

        builder.Property(p => p.IdSeriesMask).HasMaxLength(64);
        builder.Property(p => p.DrivingCategories).HasMaxLength(64);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.StatusReason).HasMaxLength(1024);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
