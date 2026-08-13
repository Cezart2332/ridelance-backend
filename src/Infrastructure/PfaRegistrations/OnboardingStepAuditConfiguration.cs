using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class OnboardingStepAuditConfiguration : IEntityTypeConfiguration<OnboardingStepAudit>
{
    public void Configure(EntityTypeBuilder<OnboardingStepAudit> builder)
    {
        builder.HasKey(a => a.Id);

        // Interogarea reală e „istoricul pasului X din dosarul Y”, deci indexul e pe pereche.
        builder.HasIndex(a => new { a.PfaRegistrationId, a.StepKey });

        builder.Property(a => a.StepKey).HasMaxLength(32).IsRequired();
        builder.Property(a => a.FromStatus).HasMaxLength(32).IsRequired();
        builder.Property(a => a.ToStatus).HasMaxLength(32).IsRequired();
        builder.Property(a => a.Note).HasMaxLength(1024);

        builder.HasOne(a => a.PfaRegistration)
            .WithMany()
            .HasForeignKey(a => a.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Urma nu se șterge odată cu contul adminului care a făcut acțiunea.
        builder.HasOne(a => a.PerformedByUser)
            .WithMany()
            .HasForeignKey(a => a.PerformedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
