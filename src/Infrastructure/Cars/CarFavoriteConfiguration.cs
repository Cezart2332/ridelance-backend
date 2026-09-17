using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Cars;

internal sealed class CarFavoriteConfiguration : IEntityTypeConfiguration<CarFavorite>
{
    public void Configure(EntityTypeBuilder<CarFavorite> builder)
    {
        builder.HasKey(f => f.Id);

        // O mașină e o singură dată la favoritele cuiva; indexul servește și la listarea lor.
        builder.HasIndex(f => new { f.UserId, f.CarId }).IsUnique();

        builder.HasOne(f => f.Car)
            .WithMany()
            .HasForeignKey(f => f.CarId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
