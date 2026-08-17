namespace Domain.Payments;

/// <summary>
/// Sumele de business, într-un singur loc. Nu în componente, nu în copy, nu în appsettings:
/// o valoare de aici se schimbă o dată și se propagă în UI prin starea de onboarding.
/// </summary>
/// <remarks>
/// Preț Stripe ≠ sumă: un <c>Price</c> Stripe e imutabil, deci orice modificare de aici cere și
/// un lookup key nou în <see cref="StripeCatalog"/> (sufix <c>_vN</c> sau suma în cheie), altfel
/// se regăsește prețul vechi și suma nouă nu are efect.
/// </remarks>
public static class Pricing
{
    /// <summary>Abonamentul RIDElance Start.</summary>
    public static class RidelanceStart
    {
        /// <summary>
        /// Avansul plătit în onboarding, înainte de transmiterea dosarului către partenerul
        /// contabil. Nerambursabil — vezi <see cref="OnboardingAdvanceIsRefundable"/>.
        /// </summary>
        public const long OnboardingAdvanceBani = 39_900;

        /// <summary>
        /// Explicit, ca UI-ul să nu decidă singur ce scrie pe badge: avansul nu se returnează.
        /// </summary>
        public const bool OnboardingAdvanceIsRefundable = false;
    }

    // Tarifele ARR (eliberare autorizație, copie conformă, ecusoane) NU sunt aici: se citesc din
    // `ArrAuthorizationRequest.FeeSnapshotBani` și `VehicleCopyRequest`, stampilate la momentul
    // cererii. Lipsa unui snapshot se afișează ca atare în UI — nu se inventează o sumă.
}
