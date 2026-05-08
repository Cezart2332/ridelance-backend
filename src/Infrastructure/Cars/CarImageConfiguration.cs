using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Cars;

internal sealed class CarImageConfiguration : IEntityTypeConfiguration<CarImage>
{
    public void Configure(EntityTypeBuilder<CarImage> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Url).HasMaxLength(1024).IsRequired();
        builder.Property(i => i.FileName).HasMaxLength(512).IsRequired();
        builder.HasIndex(i => i.CarId);
    }
}
