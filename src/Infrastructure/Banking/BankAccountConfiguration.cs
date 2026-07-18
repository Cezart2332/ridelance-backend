using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Banking;

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.HasKey(ba => ba.Id);

        builder.HasIndex(ba => ba.ProviderAccountId).IsUnique();
        builder.HasIndex(ba => ba.UserId);

        builder.Property(ba => ba.ProviderAccountId).HasMaxLength(128).IsRequired();
        builder.Property(ba => ba.IbanMasked).HasMaxLength(64);
        builder.Property(ba => ba.Currency).HasMaxLength(8);
        builder.Property(ba => ba.OwnerName).HasMaxLength(256);

        builder.HasOne(ba => ba.Connection)
            .WithMany(bc => bc.Accounts)
            .HasForeignKey(ba => ba.BankConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
