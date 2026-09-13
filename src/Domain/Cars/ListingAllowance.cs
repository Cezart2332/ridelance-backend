namespace Domain.Cars;

/// <summary>
/// Câte anunțuri active are inclus o flotă (SRL) în abonament.
/// </summary>
/// <remarks>
/// <para>
/// Anunțurile nu se mai plătesc unul câte unul: abonamentul RIDElance Fleet include
/// <see cref="IncludedInFleetPlan" /> anunțuri active simultan. Cifra e cea din descrierea planului
/// (<c>SRL_PLANS</c> din <c>src/data/plans.ts</c>, „Până la 10 anunțuri active simultan") — dacă se
/// schimbă acolo, se schimbă și aici.
/// </para>
/// <para>
/// Un loc e ocupat de un anunț pe care proprietarul l-a publicat (<see cref="ListingStatus.Published" />).
/// Ciornele, anunțurile puse pe pauză și cele scoase din flotă nu ocupă loc.
/// </para>
/// </remarks>
public static class ListingAllowance
{
    public const int IncludedInFleetPlan = 10;
}
