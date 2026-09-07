using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Users;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        /*
         * Concurența optimistă pe `xmin`, nu pe coloana jsonb.
         *
         * `FleetOnboarding` era marcat `IsConcurrencyToken()`, deci ORICE scriere pe user — inclusiv
         * salvarea refresh tokenului la login — trimitea `WHERE fleet_onboarding = @vechi`. Numai că
         * Postgres normalizează `jsonb` (reordonează cheile, scoate spațiile), în timp ce EF compară
         * cu propria serializare din C#. Textele nu coincideau, UPDATE-ul prindea zero rânduri, iar
         * `DbUpdateConcurrencyException` ieșea la client ca 409 „Datele au fost actualizate" — la
         * LOGIN, fără nicio cale de scăpare: nu era o stare veche în browser, deci nici refresh, nici
         * golirea storage-ului nu ajutau.
         *
         * `xmin` e versiunea reală de rând ținută de Postgres: se citește la fiecare SELECT, nu se
         * serializează și nu se normalizează. Aria e aceeași ca înainte (tot userul), doar
         * comparația e corectă — deci protecția cerută la checkoutul de flotă rămâne.
         */
        // Proprietate-umbră peste coloana de sistem `xmin`: e forma pe care o cere Npgsql de la
        // EF Core 7 încoace, în locul lui `UseXminAsConcurrencyToken()`. Nu are nevoie de migrație
        // — coloana există deja în orice tabel Postgres.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(u => u.FleetOnboarding).HasColumnType("jsonb")
            .HasConversion(
                value => System.Text.Json.JsonSerializer.Serialize(value, (System.Text.Json.JsonSerializerOptions?)null),
                value => System.Text.Json.JsonSerializer.Deserialize<Domain.Companies.FleetOnboarding>(value, (System.Text.Json.JsonSerializerOptions?)null) ?? new())
            .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<Domain.Companies.FleetOnboarding>(
                (a, b) => System.Text.Json.JsonSerializer.Serialize(a, (System.Text.Json.JsonSerializerOptions?)null) == System.Text.Json.JsonSerializer.Serialize(b, (System.Text.Json.JsonSerializerOptions?)null),
                value => System.Text.Json.JsonSerializer.Serialize(value, (System.Text.Json.JsonSerializerOptions?)null).GetHashCode(),
                value => System.Text.Json.JsonSerializer.Deserialize<Domain.Companies.FleetOnboarding>(System.Text.Json.JsonSerializer.Serialize(value, (System.Text.Json.JsonSerializerOptions?)null), (System.Text.Json.JsonSerializerOptions?)null)!));

        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.FirstName).HasMaxLength(128).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(128).IsRequired();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.PhoneNumber).HasMaxLength(32);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(32);
        builder.Property(u => u.RefreshToken).HasMaxLength(256);
        builder.Property(u => u.EmailVerificationCode).HasMaxLength(16);

        builder.Property(u => u.PhoneVerificationCode).HasMaxLength(16);

        // Proprietăți calculate din datele de confirmare; nu au coloană.
        builder.Ignore(u => u.IsEmailVerified);
        builder.Ignore(u => u.IsPhoneVerified);
    }
}
