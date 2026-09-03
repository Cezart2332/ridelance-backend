using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

internal sealed class PfaPlatformAccountConfiguration : IEntityTypeConfiguration<PfaPlatformAccount>
{
    public void Configure(EntityTypeBuilder<PfaPlatformAccount> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => new { a.PfaRegistrationId, a.Provider, a.Kind }).IsUnique();

        builder.Property(a => a.Provider).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Email).HasMaxLength(256);
        builder.Property(a => a.Phone).HasMaxLength(32);
        builder.Property(a => a.FullName).HasMaxLength(256);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.OperatorAccountId).HasMaxLength(128);
        builder.Property(a => a.PasswordProtected).HasMaxLength(1024);
        builder.Property(a => a.ExistingAccountAnswer).HasMaxLength(32);
        builder.Property(a => a.OnboardingStatus).HasConversion<string>().HasMaxLength(32);

        // Contul de șofer, pe aceeași linie cu cel de flotă: e aceeași platformă, aceeași
        // înregistrare, doar alt rol — o a doua linie ar cere un al doilea Kind și ar strica
        // indexul unic de mai sus.
        builder.Property(a => a.DriverEmail).HasMaxLength(256);
        builder.Property(a => a.DriverPhone).HasMaxLength(32);
        builder.Property(a => a.DriverFullName).HasMaxLength(256);
        builder.Property(a => a.DriverExternalId).HasMaxLength(128);

        builder.HasOne(a => a.PfaRegistration)
            .WithMany(r => r.PlatformAccounts)
            .HasForeignKey(a => a.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
