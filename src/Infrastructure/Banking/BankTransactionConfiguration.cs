using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Banking;

internal sealed class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.HasKey(bt => bt.Id);

        builder.HasIndex(bt => new { bt.BankAccountId, bt.ProviderTransactionId }).IsUnique();
        builder.HasIndex(bt => new { bt.UserId, bt.BookingDate });

        builder.Property(bt => bt.ProviderTransactionId).HasMaxLength(256).IsRequired();
        builder.Property(bt => bt.Amount).HasColumnType("decimal(18,2)");
        builder.Property(bt => bt.Currency).HasMaxLength(8).IsRequired();
        builder.Property(bt => bt.CounterpartyName).HasMaxLength(256);
        builder.Property(bt => bt.RemittanceInfo).HasMaxLength(1024);
        builder.Property(bt => bt.RawJson).HasColumnType("jsonb");
        builder.Property(bt => bt.Category).HasMaxLength(64);
        builder.Property(bt => bt.MatchedSource).HasMaxLength(32);

        builder.HasOne(bt => bt.Account)
            .WithMany()
            .HasForeignKey(bt => bt.BankAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
