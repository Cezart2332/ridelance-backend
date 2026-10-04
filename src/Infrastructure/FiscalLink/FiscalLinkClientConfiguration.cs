using Domain.FiscalLink;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.FiscalLink;

internal sealed class FiscalLinkClientConfiguration : IEntityTypeConfiguration<FiscalLinkClient>
{
    public void Configure(EntityTypeBuilder<FiscalLinkClient> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.LastSyncError).HasMaxLength(1000);

        // Un PFA, un client FiscalLink: al doilea „Conectează” nu face un comerciant dublură.
        builder.HasIndex(c => c.PfaRegistrationId).IsUnique();
        builder.HasIndex(c => c.UserId);

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.PfaRegistration)
            .WithMany()
            .HasForeignKey(c => c.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
