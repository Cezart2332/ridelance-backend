using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Cars;

internal sealed class CarLeadConfiguration : IEntityTypeConfiguration<CarLead>
{
    public void Configure(EntityTypeBuilder<CarLead> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.CarName).HasMaxLength(512).IsRequired();
        builder.Property(l => l.UserName).HasMaxLength(256).IsRequired();
        builder.Property(l => l.UserEmail).HasMaxLength(256).IsRequired();
        builder.Property(l => l.UserPhone).HasMaxLength(32).IsRequired();
        builder.Property(l => l.InterestType).HasMaxLength(64).IsRequired();
        builder.Property(l => l.AdminNote).HasMaxLength(2048);
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(l => l.CarId);
        builder.HasIndex(l => l.Status);
    }
}
