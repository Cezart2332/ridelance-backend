using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.PfaRegistrations;

/// <summary>
/// Maparea owned types-urilor reutilizate de dosarul de înființare. <see cref="Adresa"/> apare
/// în patru locuri și <see cref="PersoanaFizica"/> în două — o singură definiție, ca lungimile
/// de coloană să nu divergă între ele.
/// </summary>
internal static class CompanyFormationMapping
{
    public static void ConfigureAdresa<TOwner>(OwnedNavigationBuilder<TOwner, Adresa> builder)
        where TOwner : class
    {
        builder.Property(a => a.Judet).HasMaxLength(64);
        builder.Property(a => a.Localitate).HasMaxLength(128);
        builder.Property(a => a.Strada).HasMaxLength(256);
        builder.Property(a => a.Numar).HasMaxLength(32);
        builder.Property(a => a.Bloc).HasMaxLength(32);
        builder.Property(a => a.Scara).HasMaxLength(32);
        builder.Property(a => a.Etaj).HasMaxLength(32);
        builder.Property(a => a.Apartament).HasMaxLength(32);
    }

    public static void ConfigurePersoana<TOwner>(OwnedNavigationBuilder<TOwner, PersoanaFizica> builder)
        where TOwner : class
    {
        builder.Property(p => p.Nume).HasMaxLength(100);
        builder.Property(p => p.Prenume).HasMaxLength(100);

        // CNP-ul complet există doar criptat; în clar rămâne exclusiv masca.
        builder.Property(p => p.CnpEncrypted).HasMaxLength(512);
        builder.Property(p => p.CnpMasked).HasMaxLength(32);

        builder.Property(p => p.TipAct).HasConversion<string>().HasMaxLength(24);
        builder.Property(p => p.SerieAct).HasMaxLength(8);
        builder.Property(p => p.NumarAct).HasMaxLength(16);
        builder.Property(p => p.AutoritateEmitenta).HasMaxLength(150);

        builder.OwnsOne(p => p.Domiciliu, ConfigureAdresa);
    }
}
