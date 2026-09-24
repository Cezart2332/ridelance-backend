using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class OnboardingAnswerConfiguration : IEntityTypeConfiguration<OnboardingAnswer>
{
    public void Configure(EntityTypeBuilder<OnboardingAnswer> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.UserId, a.QuestionId, a.AnsweredAtUtc });
        builder.Property(a => a.StepKey).HasMaxLength(32);
        builder.Property(a => a.QuestionId).HasMaxLength(96);
        builder.Property(a => a.Question).HasMaxLength(OnboardingAnswerLimits.Question);
        builder.Property(a => a.Value).HasMaxLength(OnboardingAnswerLimits.Value);
        builder.Property(a => a.ValueLabel).HasMaxLength(OnboardingAnswerLimits.Value);
    }
}
