using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Users;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.FirstName).HasMaxLength(128).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(128).IsRequired();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.PhoneNumber).HasMaxLength(32);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(32);
        builder.Property(u => u.RefreshToken).HasMaxLength(256);
    }
}
