namespace Application.Cars.Scoring;

/// <summary>
/// Ponderile sortării „Recomandate", citite din <c>Marketplace:Scoring</c>.
/// </summary>
/// <remarks>
/// Stau în configurare, nu în cod, pentru că sunt reglaje de business: cât valorează o poză în
/// plus față de o reducere se află abia după ce marketplace-ul are trafic, iar răspunsul nu
/// merită un deploy (spec §5.2).
///
/// Valorile implicite sunt cele din spec.
/// </remarks>
public sealed class RecommendationScoringOptions
{
    public const string SectionName = "Marketplace:Scoring";

    /// <summary>Descriere completă: cel puțin <see cref="DescriptionFullMinLength"/> caractere reale.</summary>
    public int DescriptionFull { get; set; } = 30;
    public int DescriptionPartial { get; set; } = 15;
    public int DescriptionFullMinLength { get; set; } = 200;
    public int DescriptionPartialMinLength { get; set; } = 60;

    public int PhotosMany { get; set; } = 15;
    public int PhotosFew { get; set; } = 8;
    public int PhotosManyMin { get; set; } = 6;
    public int PhotosFewMin { get; set; } = 3;

    public int DiscountActive { get; set; } = 20;
    public int AvailableNow { get; set; } = 5;
    public int OwnerVerified { get; set; } = 5;
    public int OwnerHasLogo { get; set; } = 5;

    /// <summary>
    /// Prospețimea: un anunț neatins de luni de zile e mai puțin probabil să fie încă valabil.
    /// Se aplică multiplicativ pe totalul de puncte.
    /// </summary>
    public double FreshnessRecent { get; set; } = 1.0;
    public double FreshnessStale { get; set; } = 0.9;
    public double FreshnessOld { get; set; } = 0.75;
    public int FreshnessRecentDays { get; set; } = 7;
    public int FreshnessStaleDays { get; set; } = 30;

    /// <summary>
    /// Pinul de preluare, setat pe hartă (spec §5.2). A devenit măsurabil odată cu fluxul de
    /// adăugare pe pași, care salvează coordonate, nu doar orașul ca text.
    /// </summary>
    public int MapPin { get; set; } = 10;

    /// <summary>
    /// Completitudinea dosarului de vehicul. Pragul e proporția câmpurilor administrative
    /// completate — număr, VIN, kilometraj, primă înmatriculare.
    /// </summary>
    public int VehicleDossier { get; set; } = 10;

    /// <summary>Cât din dosar trebuie completat ca să conteze. Spec §5.2 cere 80%.</summary>
    public double VehicleDossierThreshold { get; set; } = 0.8;
}
