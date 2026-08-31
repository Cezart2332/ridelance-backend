using System.Text.Json;
using Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Companies;

internal sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.OwnerType).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.LegalName).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Cui).HasMaxLength(32);
        builder.Property(c => c.Iban).HasMaxLength(34);
        builder.Property(c => c.RegCom).HasMaxLength(64);
        builder.Property(c => c.LegalRepresentative).HasMaxLength(256);
        builder.Property(c => c.RegisteredOffice).HasMaxLength(512);
        builder.Property(c => c.Phone).HasMaxLength(32);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Website).HasMaxLength(256);
        builder.Property(c => c.PublicDescription).HasMaxLength(2048);
        builder.Property(c => c.Tagline).HasMaxLength(160);
        builder.Property(c => c.LogoUrl).HasMaxLength(512);
        builder.Property(c => c.CoverImageUrl).HasMaxLength(512);
        builder.Property(c => c.PickupAddress).HasMaxLength(512);
        builder.Property(c => c.PickupNote).HasMaxLength(600);
        builder.Property(c => c.Slug).HasMaxLength(160).IsRequired();

        // Personalizarea mini-site-ului stă în jsonb, ca listele de pe anunț (vezi CarConfiguration).
        // Sunt valori care se citesc întotdeauna împreună cu profilul și nu se caută niciodată după
        // ele — un tabel separat ar fi adăugat un join la fiecare deschidere a paginii publice.
        //
        // Comparatorul serializează pentru comparație: fără el, EF nu observă că s-a schimbat o
        // culoare, fiindcă referința obiectului rămâne aceeași.
        ConfigureJson(builder.Property(c => c.PageTheme));
        ConfigureJson(builder.Property(c => c.PageContent));

        // Verdictul de moderare și copia aprobată a paginii, tot jsonb: se citesc împreună cu
        // profilul la fiecare deschidere a paginii publice și nu se caută niciodată după ele.
        ConfigureJson(builder.Property(c => c.PageModeration));
        ConfigureJson(builder.Property(c => c.PublishedPage));

        // Un cont are cel mult un profil, iar slug-ul e identitatea publică: ambele unice.
        builder.HasIndex(c => c.UserId).IsUnique();
        builder.HasIndex(c => c.Slug).IsUnique();
    }

    private static void ConfigureJson<T>(PropertyBuilder<T> property)
        where T : class, new()
    {
        property
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<T>(v, (JsonSerializerOptions?)null) ?? new T())
            .Metadata.SetValueComparer(new ValueComparer<T>(
                (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) ==
                          JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(StringComparison.Ordinal),
                v => JsonSerializer.Deserialize<T>(
                         JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                         (JsonSerializerOptions?)null) ?? new T()));
    }
}
