using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class ArrAuthorizationRequestConfiguration : IEntityTypeConfiguration<ArrAuthorizationRequest>
{
    public void Configure(EntityTypeBuilder<ArrAuthorizationRequest> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => a.PfaRegistrationId).IsUnique();

        builder.Property(a => a.AgencyName).HasMaxLength(256);
        builder.Property(a => a.SubmissionMethod).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.AuthorizationNumber).HasMaxLength(64);
        builder.Property(a => a.AdminNote).HasMaxLength(1024);

        builder.HasOne(a => a.PfaRegistration)
            .WithOne(r => r.ArrAuthorizationRequest)
            .HasForeignKey<ArrAuthorizationRequest>(a => a.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
