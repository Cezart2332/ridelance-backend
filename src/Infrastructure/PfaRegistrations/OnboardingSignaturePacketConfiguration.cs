using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class OnboardingSignaturePacketConfiguration : IEntityTypeConfiguration<OnboardingSignaturePacket>
{
    public void Configure(EntityTypeBuilder<OnboardingSignaturePacket> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasIndex(p => p.PfaRegistrationId).IsUnique();

        builder.Property(p => p.Provider).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.ProviderReference).HasMaxLength(256);
        builder.Property(p => p.AdminNote).HasMaxLength(1024);

        builder.HasOne(p => p.PfaRegistration)
            .WithOne(r => r.SignaturePacket)
            .HasForeignKey<OnboardingSignaturePacket>(p => p.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Documents)
            .WithOne(d => d.Packet)
            .HasForeignKey(d => d.PacketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OnboardingSignatureDocumentConfiguration : IEntityTypeConfiguration<OnboardingSignatureDocument>
{
    public void Configure(EntityTypeBuilder<OnboardingSignatureDocument> builder)
    {
        builder.HasKey(d => d.Id);

        builder.HasIndex(d => d.PacketId);

        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Label).HasMaxLength(256);
    }
}
