namespace Domain.PfaRegistrations.ArrFleet;

/// <summary>
/// Plata pasului, o sumă întreagă: autorizația de transport (3 ani), copia conformă (1 an) și
/// ecusoanele, câte un set pe platformă. O platformă = 408 lei, ambele = 416 lei.
/// </summary>
public static class ArrFleetPricing
{
    public const long TransportAuthorizationBani = 30_000;
    public const long CertifiedCopyBani = 10_000;
    public const long BadgePerPlatformBani = 800;

    public static long AmountBani(ArrFleetPlatforms platforms)
    {
        int count = (platforms.HasFlag(ArrFleetPlatforms.Uber) ? 1 : 0) + (platforms.HasFlag(ArrFleetPlatforms.Bolt) ? 1 : 0);
        return count == 0 ? 0 : TransportAuthorizationBani + CertifiedCopyBani + BadgePerPlatformBani * count;
    }
}
