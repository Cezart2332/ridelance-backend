using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class PfaBankAccountDeclarationConfiguration : IEntityTypeConfiguration<PfaBankAccountDeclaration>
{
    public void Configure(EntityTypeBuilder<PfaBankAccountDeclaration> builder)
    {
        builder.HasKey(d => d.Id);

        builder.HasIndex(d => d.PfaRegistrationId).IsUnique();

        builder.Property(d => d.BankName).HasMaxLength(128);
        builder.Property(d => d.IbanEncrypted).HasMaxLength(512);
        builder.Property(d => d.IbanMasked).HasMaxLength(64);
        builder.Property(d => d.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.AdminNote).HasMaxLength(1024);

        builder.HasOne(d => d.PfaRegistration)
            .WithOne(r => r.BankAccountDeclaration)
            .HasForeignKey<PfaBankAccountDeclaration>(d => d.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
