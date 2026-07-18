using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Invoicing;

internal sealed class IssuedInvoiceConfiguration : IEntityTypeConfiguration<IssuedInvoice>
{
    public void Configure(EntityTypeBuilder<IssuedInvoice> builder)
    {
        builder.HasKey(i => i.Id);

        builder.HasIndex(i => i.PaymentRecordId);
        builder.HasIndex(i => i.ServiceOrderId);
        builder.HasIndex(i => i.UserId);
        builder.HasIndex(i => i.CreatedAtUtc);

        builder.Property(i => i.ClientName).HasMaxLength(256);
        builder.Property(i => i.ClientCif).HasMaxLength(32);
        builder.Property(i => i.ClientEmail).HasMaxLength(256);
        builder.Property(i => i.Description).HasMaxLength(512);
        builder.Property(i => i.Currency).HasMaxLength(8);
        builder.Property(i => i.SeriesName).HasMaxLength(32);
        builder.Property(i => i.Number).HasMaxLength(32);
        builder.Property(i => i.Link).HasMaxLength(512);
        builder.Property(i => i.ErrorMessage).HasMaxLength(1024);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
    }
}
