using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

/// <summary>
/// Datele solicitantului și ale sediului sunt owned types: sunt 1:1 cu dosarul, deci se
/// aplatizează în același tabel în loc să coste două join-uri la fiecare citire.
/// </summary>
internal sealed class CompanyFormationRequestConfiguration : IEntityTypeConfiguration<CompanyFormationRequest>
{
    public void Configure(EntityTypeBuilder<CompanyFormationRequest> builder)
    {
        builder.HasKey(r => r.Id);

        builder.HasIndex(r => r.PfaRegistrationId).IsUnique();

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(r => r.CurrentStage).HasConversion<string>().HasMaxLength(24);
        builder.Property(r => r.OfficeType).HasConversion<string>().HasMaxLength(24);
        builder.Property(r => r.PrefilledFields).HasColumnType("jsonb");
        builder.Property(r => r.AdminNote).HasMaxLength(1024);
        builder.Property(r => r.IdentityMismatchNote).HasMaxLength(512);
        builder.Property(r => r.ConsultoSendStripeEventId).HasMaxLength(128);

        builder.OwnsOne(r => r.Solicitant, CompanyFormationMapping.ConfigurePersoana);
        builder.OwnsOne(r => r.OfficeAddress, CompanyFormationMapping.ConfigureAdresa);

        builder.HasOne(r => r.PfaRegistration)
            .WithOne(p => p.CompanyFormationRequest)
            .HasForeignKey<CompanyFormationRequest>(r => r.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Adresele Consulto rămân referite de dosarele vechi chiar dacă sunt dezactivate.
        builder.HasOne(r => r.ConsultoOffice)
            .WithMany()
            .HasForeignKey(r => r.ConsultoOfficeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Owners)
            .WithOne(o => o.CompanyFormationRequest)
            .HasForeignKey(o => o.CompanyFormationRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Consents)
            .WithOne(c => c.CompanyFormationRequest)
            .HasForeignKey(c => c.CompanyFormationRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Signature)
            .WithOne(s => s.CompanyFormationRequest)
            .HasForeignKey<CompanyFormationSignature>(s => s.CompanyFormationRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyFormationOwnerConfiguration : IEntityTypeConfiguration<CompanyFormationOwner>
{
    public void Configure(EntityTypeBuilder<CompanyFormationOwner> builder)
    {
        builder.HasKey(o => o.Id);
        builder.HasIndex(o => o.CompanyFormationRequestId);

        builder.OwnsOne(o => o.Persoana, CompanyFormationMapping.ConfigurePersoana);
    }
}

internal sealed class CompanyFormationConsentConfiguration : IEntityTypeConfiguration<CompanyFormationConsent>
{
    public void Configure(EntityTypeBuilder<CompanyFormationConsent> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.CompanyFormationRequestId);

        builder.Property(c => c.StepKey).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Version).HasMaxLength(32).IsRequired();
        builder.Property(c => c.TextSnapshot).IsRequired();
        builder.Property(c => c.CheckboxLabelSnapshot).HasMaxLength(512);
    }
}

internal sealed class CompanyFormationSignatureConfiguration : IEntityTypeConfiguration<CompanyFormationSignature>
{
    public void Configure(EntityTypeBuilder<CompanyFormationSignature> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.CompanyFormationRequestId).IsUnique();

        builder.Property(s => s.VectorData).HasColumnType("jsonb");
        builder.Property(s => s.IpAddress).HasMaxLength(64);
        builder.Property(s => s.UserAgent).HasMaxLength(512);
        builder.Property(s => s.DeviceType).HasMaxLength(32);
        builder.Property(s => s.Os).HasMaxLength(64);
        builder.Property(s => s.Browser).HasMaxLength(64);
        builder.Property(s => s.PayloadHash).HasMaxLength(64).IsRequired();

        // Retrimiterea aceleiași chei nu creează a doua semnătură.
        builder.Property(s => s.IdempotencyKey).HasMaxLength(128);
        builder.HasIndex(s => s.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL");
    }
}

internal sealed class ConsultoOfficeConfiguration : IEntityTypeConfiguration<ConsultoOffice>
{
    public void Configure(EntityTypeBuilder<ConsultoOffice> builder)
    {
        builder.HasKey(o => o.Id);
        builder.OwnsOne(o => o.Adresa, CompanyFormationMapping.ConfigureAdresa);
    }
}

internal sealed class LegalConsentFlowConfiguration : IEntityTypeConfiguration<LegalConsentFlow>
{
    public void Configure(EntityTypeBuilder<LegalConsentFlow> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Context).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Version).HasMaxLength(32).IsRequired();

        builder.HasIndex(f => new { f.Context, f.Version }).IsUnique();

        builder.HasMany(f => f.Steps)
            .WithOne(s => s.LegalConsentFlow)
            .HasForeignKey(s => s.LegalConsentFlowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LegalConsentStepConfiguration : IEntityTypeConfiguration<LegalConsentStep>
{
    public void Configure(EntityTypeBuilder<LegalConsentStep> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.LegalConsentFlowId);

        builder.Property(s => s.Key).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Title).HasMaxLength(256).IsRequired();
        builder.Property(s => s.Subtitle).HasMaxLength(256);
        builder.Property(s => s.Body).IsRequired();
        builder.Property(s => s.CheckboxLabel).HasMaxLength(512).IsRequired();
    }
}
