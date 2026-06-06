using Domain.Bolt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Bolt;

internal sealed class BoltIntegrationConfiguration : IEntityTypeConfiguration<BoltIntegration>
{
    public void Configure(EntityTypeBuilder<BoltIntegration> builder)
    {
        builder.HasKey(bi => bi.Id);

        builder.HasIndex(bi => bi.UserId).IsUnique(); // One integration per user

        builder.Property(bi => bi.ClientId).HasMaxLength(256).IsRequired();
        builder.Property(bi => bi.ClientSecret).HasMaxLength(256).IsRequired();
        builder.Property(bi => bi.CompanyId).IsRequired();
        builder.Property(bi => bi.CompanyName).HasMaxLength(256);
        builder.Property(bi => bi.AccessToken).HasMaxLength(2048);
        builder.Property(bi => bi.ErrorMessage).HasMaxLength(1024);

        builder.HasOne(bi => bi.User)
            .WithMany()
            .HasForeignKey(bi => bi.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
