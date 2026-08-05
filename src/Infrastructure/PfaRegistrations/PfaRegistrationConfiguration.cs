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
        builder.Property(r => r.PfaSource).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.LegalName).HasMaxLength(256);
        builder.Property(r => r.RegistryNumber).HasMaxLength(64);
        builder.Property(r => r.CaenCodes).HasColumnType("jsonb");
        builder.Property(r => r.HolderName).HasMaxLength(256);
        builder.Property(r => r.ProfessionalOffice).HasMaxLength(512);
        builder.Property(r => r.AuthorizedActivities).HasMaxLength(2048);
        builder.Property(r => r.ActivityLocation).HasMaxLength(256);
        builder.Property(r => r.WorkPoints).HasMaxLength(1024);
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

        builder.HasOne(r => r.PartnerLead)
            .WithOne(l => l.PfaRegistration)
            .HasForeignKey<PfaPartnerLead>(l => l.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
