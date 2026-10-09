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
    /// <summary>Avansul plătit în onboarding.</summary>
    public static class OnboardingAdvance
    {
        /// <summary>
        /// Avansul plătit în onboarding, egal cu prima lună de PFA Full. Se cere pe ambele
        /// ramuri — și cine are deja PFA, și cine îl deschide prin noi — fiindcă e avans pe
        /// abonament, nu taxă de înființare. Nerambursabil, dar se întoarce integral ca reducere
        /// la primul abonament: vezi <see cref="OnboardingAdvanceCredit"/>.
        /// </summary>
        public const long OnboardingAdvanceBani = Plans.PfaFullMonthlyBani;

        /// <summary>
        /// Explicit, ca UI-ul să nu decidă singur ce scrie pe badge: avansul nu se returnează.
        /// </summary>
        public const bool OnboardingAdvanceIsRefundable = false;

        /// <summary>
        /// Descrierea cu care se înregistrează plata avansului.
        ///
        /// Constantă, nu text scris în două locuri: plata se poate face ÎNAINTE să existe un
        /// dosar (întrebarea „ai deja PFA?" vine după), deci rândul rămâne o vreme fără
        /// <c>PfaRegistrationId</c>, iar „a plătit avansul?" se răspunde pe descriere. Dacă
        /// scrierea și citirea ar folosi texte diferite, clientul ar fi pus să plătească a doua
        /// oară.
        /// </summary>
        public const string OnboardingAdvanceDescription = "Avans abonament RIDElance";

        /// <summary>Descrierea avansului cât timp era o lună de RIDElance Start (399 lei).</summary>
        public const string LegacyStartDescription = "Avans abonament RIDElance Start";

        /// <summary>Descrierea folosită înainte ca avansul să se ceară pe ambele ramuri.</summary>
        public const string LegacyInfiintareDescription = "Înființare PFA";

        /// <summary>Toate descrierile sub care s-a înregistrat vreodată avansul.</summary>
        public static readonly string[] AllDescriptions =
            [OnboardingAdvanceDescription, LegacyStartDescription, LegacyInfiintareDescription];
    }

    /// <summary>
    /// Cum se întoarce avansul de <see cref="OnboardingAdvance.OnboardingAdvanceBani"/> la primul
    /// abonament ales la finalul onboardingului.
    ///
    /// Nu e un singur cupon de 299 lei „once": Stripe nu reportează restul unei reduceri pe
    /// factura următoare, deci pe PFAlone (139/lună) primul cupon ar fi înghițit 299 pentru o
    /// factură de 139, iar a doua lună s-ar fi facturat întreagă. De aceea fiecare plan are
    /// forma lui: PFA Full o lună gratis, PFAlone două. Din luna următoare, preț normal.
    /// </summary>
    public static class OnboardingAdvanceCredit
    {
        /// <param name="AmountOffBani">Cât se scade de pe fiecare factură acoperită.</param>
        /// <param name="Months">Câte facturi acoperă. 1 = doar prima.</param>
        public readonly record struct Spec(string CouponId, string Name, long AmountOffBani, int Months);

        /// <summary>
        /// Forma reducerii pentru un plan, sau <c>null</c> dacă planul n-are una (flota nu trece
        /// prin onboardingul PFA, deci n-a plătit avansul).
        ///
        /// Id-urile poartă suma, ca la <see cref="BcrDiscount.StripeCouponId"/>: un cupon Stripe e
        /// imutabil, deci o valoare nouă are nevoie de un id nou, altfel se regăsește cel vechi.
        /// </summary>
        public static Spec? For(string plan) => plan?.ToUpperInvariant() switch
        {
            // Două luni întregi. 2 × 139 = 278, sub avansul de 299 — un cupon de 149,50 ar fi
            // arătat o sumă inexistentă pe factură pentru exact aceleași două luni gratuite.
            "PFALONE" => new Spec(
                "ridelance_avans_pfalone_139ron_2m", "RIDElance — avans onboarding (PFAlone)",
                Plans.PfaAloneMonthlyBani, 2),
            // Avansul E prețul planului: exact o lună.
            "PFA-FULL" => new Spec(
                "ridelance_avans_pfafull_299ron_1m", "RIDElance — avans onboarding (PFA Full)",
                Plans.PfaFullMonthlyBani, 1),
            _ => null,
        };
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
        /// <summary>PFAlone: șoferul își ține singur evidența, cu generatorul de declarații.</summary>
        public const long PfaAloneMonthlyBani = 13_900;

        /// <summary>PFA Full: RIDElance se ocupă de toată partea fiscală.</summary>
        public const long PfaFullMonthlyBani = 29_900;

        /// <summary>Reducerea la plata anuală, ca fracție. Aceeași valoare ca `ANNUAL_DISCOUNT`.</summary>
        public const decimal AnnualDiscount = 0.10m;

        // Totalul facturat o dată pe an: 12 luni cu reducerea aplicată. Scris explicit, nu calculat:
        // un `Price` Stripe are nevoie de un întreg exact, iar rotunjirea nu are voie să depindă de
        // ordinea operațiilor.
        public const long PfaAloneAnnualBani = 150_120;
        public const long PfaFullAnnualBani = 322_920;
    }

    /// <summary>
    /// Opțiunile plătite ale PFAlone, peste abonament. La PFA Full sunt incluse. Anual, cu aceeași
    /// reducere de 10% ca planul, ca să se poată factura pe același abonament Stripe.
    /// </summary>
    public static class Addons
    {
        /// <summary>Conectarea contului bancar prin Open Banking.</summary>
        public const long OpenBankingMonthlyBani = 4_900;
        public const long OpenBankingAnnualBani = 52_920;

        /// <summary>Automatizarea casei de marcat.</summary>
        public const long CashRegisterMonthlyBani = 4_900;
        public const long CashRegisterAnnualBani = 52_920;
    }

    /// <summary>
    /// Reducerea pentru clienții care își deschid cont BCR prin RIDElance.
    ///
    /// Nu se aplică la bifă, ci după ce BCR confirmă contul: până atunci nu avem de unde ști dacă
    /// s-a deschis. De aceea bifa de la checkout doar înregistrează intenția, iar suma încasată
    /// atunci rămâne întreagă.
    /// </summary>
    /// <summary>Opțiunile plătite ale anunțurilor de flotă.</summary>
    public static class PaidExtras
    {
        /// <summary>Un anunț peste cele incluse în abonament: 39,90 lei pe lună, per mașină.</summary>
        public const long ExtraListingMonthlyBani = 3_990;

        /// <summary>
        /// Numărul de înmatriculare ascuns în anunț: 14,90 lei o singură dată, per mașină. Rămâne
        /// ascuns la orice republicare a aceleiași mașini.
        /// </summary>
        public const long HiddenPlateBani = 1_490;
    }

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

    // Tarifele ARR (autorizație, copie conformă, ecusoane) sunt în `ArrFleetPricing`: se plătesc
    // direct agenției ARR, nu nouă.

    /// <summary>
    /// Serviciile individuale, fără abonament — de pe site sau din dashboard. Înființarea și
    /// Start Ride cer aceleași date ca ramura „Nu am PFA" din onboarding, iar dosarul pleacă
    /// singur spre Consulto după plată.
    /// </summary>
    public static class Services
    {
        public const long InfiintarePfaBani = 24_900;

        /// <summary>Tarif anual, încasat o dată pe an ca plată unică — nu ca abonament Stripe.</summary>
        public const long SediuSocialAnnualBani = 34_900;

        /// <summary>Înființarea PFA și înregistrarea în scopuri de TVA intracomunitar sunt incluse.</summary>
        public const long StartRideBani = 99_900;
    }
}
