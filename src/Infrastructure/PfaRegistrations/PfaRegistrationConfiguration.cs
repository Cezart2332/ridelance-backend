using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class PfaRegistrationConfiguration : IEntityTypeConfiguration<PfaRegistration>
{
    public void Configure(EntityTypeBuilder<PfaRegistration> builder)
    {
        builder.HasKey(r => r.Id);

        builder.HasIndex(r => r.UserId);

        builder.Property(r => r.RegistrationType).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.FullName).HasMaxLength(256);
        builder.Property(r => r.Phone).HasMaxLength(32);
        builder.Property(r => r.Cui).HasMaxLength(32);
        builder.Property(r => r.Street).HasMaxLength(256);
        builder.Property(r => r.Number).HasMaxLength(32);
        builder.Property(r => r.City).HasMaxLength(128);
        builder.Property(r => r.County).HasMaxLength(128);
        builder.Property(r => r.ReviewNote).HasMaxLength(1024);

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.AssignedContabil)
            .WithMany()
            .HasForeignKey(r => r.AssignedContabilId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(r => r.Documents)
            .WithOne()
            .HasForeignKey(d => d.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
