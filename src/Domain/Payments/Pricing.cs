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

    /// <summary>
    /// Abonamentele lunare și anuale, în bani.
    ///
    /// Sumele sunt cele anunțate public (`src/data/plans.ts` în frontend): plata lunară, cu 10%
    /// reducere la plata anuală. Până acum catalogul Stripe încasa săptămânal (49/99/149 lei),
    /// deci pagina anunța un model pe care casa nu-l putea onora — de aici încolo e o singură sumă.
    /// </summary>
    public static class Plans
    {
        public const long SoloMonthlyBani = 19_900;
        public const long StartMonthlyBani = 39_900;
        public const long ProMonthlyBani = 59_900;

        /// <summary>Reducerea la plata anuală, ca fracție. Aceeași valoare ca `ANNUAL_DISCOUNT`.</summary>
        public const decimal AnnualDiscount = 0.10m;

        // Totalul facturat o dată pe an: 12 luni cu reducerea aplicată. Scris explicit, nu calculat:
        // un `Price` Stripe are nevoie de un întreg exact, iar rotunjirea nu are voie să depindă de
        // ordinea operațiilor.
        public const long SoloAnnualBani = 214_920;
        public const long StartAnnualBani = 430_920;
        public const long ProAnnualBani = 646_920;
    }

    /// <summary>
    /// Reducerea pentru clienții care își deschid cont BCR prin RIDElance.
    ///
    /// Nu se aplică la bifă, ci după ce BCR confirmă contul: până atunci nu avem de unde ști dacă
    /// s-a deschis. De aceea bifa de la checkout doar înregistrează intenția, iar suma încasată
    /// atunci rămâne întreagă.
    /// </summary>
    public static class BcrDiscount
    {
        public const long MonthlyBani = 5_000;
        public const int Months = 6;

        /// <summary>
        /// Id-ul cuponului din Stripe. Fix, nu generat: cuponul e același pentru toți clienții, iar
        /// unul nou la fiecare confirmare ar umple contul cu duplicate identice.
        ///
        /// Suma e în id din același motiv pentru care e în lookup key-urile din
        /// <see cref="StripeCatalog"/>: un cupon Stripe e imutabil, deci o valoare nouă cere un id
        /// nou, altfel se regăsește cel vechi și reducerea nu se schimbă.
        /// </summary>
        public const string StripeCouponId = "ridelance_bcr_50ron_6m";
    }

    // Tarifele ARR (eliberare autorizație, copie conformă, ecusoane) NU sunt aici: se citesc din
    // `ArrAuthorizationRequest.FeeSnapshotBani` și `VehicleCopyRequest`, stampilate la momentul
    // cererii. Lipsa unui snapshot se afișează ca atare în UI — nu se inventează o sumă.
}
