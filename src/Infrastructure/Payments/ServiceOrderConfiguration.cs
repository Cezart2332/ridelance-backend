using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Payments;

internal sealed class ServiceOrderConfiguration : IEntityTypeConfiguration<ServiceOrder>
{
    public void Configure(EntityTypeBuilder<ServiceOrder> builder)
    {
        builder.HasKey(o => o.Id);

        builder.HasIndex(o => o.StripeSessionId);
        builder.HasIndex(o => o.CustomerEmail);

        builder.Property(o => o.ServiceKey).HasMaxLength(64).IsRequired();
        builder.Property(o => o.ServiceTitle).HasMaxLength(256).IsRequired();
        builder.Property(o => o.CustomerName).HasMaxLength(256).IsRequired();
        builder.Property(o => o.CustomerEmail).HasMaxLength(256).IsRequired();
        builder.Property(o => o.CustomerPhone).HasMaxLength(32).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.StripeSessionId).HasMaxLength(128);
    }
}
