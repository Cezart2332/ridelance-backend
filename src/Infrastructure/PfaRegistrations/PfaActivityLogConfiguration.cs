using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class PfaActivityLogConfiguration : IEntityTypeConfiguration<PfaActivityLog>
{
    public void Configure(EntityTypeBuilder<PfaActivityLog> builder)
    {
        builder.HasKey(l => l.Id);

        builder.HasOne(l => l.PfaRegistration)
            .WithMany()
            .HasForeignKey(l => l.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.PerformedByUser)
            .WithMany()
            .HasForeignKey(l => l.PerformedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
