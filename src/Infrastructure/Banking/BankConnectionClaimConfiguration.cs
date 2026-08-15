using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Banking;

internal sealed class BankConnectionClaimConfiguration : IEntityTypeConfiguration<BankConnectionClaim>
{
    public void Configure(EntityTypeBuilder<BankConnectionClaim> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ProviderConnectionId).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Mode).HasConversion<string>().HasMaxLength(16).IsRequired();

        // Plasa de siguranță a întregului model: o conexiune nu poate avea doi proprietari,
        // oricât ar greși logica de deducție de deasupra.
        builder.HasIndex(c => c.ProviderConnectionId).IsUnique();

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Connection)
            .WithMany()
            .HasForeignKey(c => c.BankConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
