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
    /// Criteriile din spec §5.2 care nu au încă sursă de date: pinul pe hartă (<c>Car.Location</c>
    /// e doar oraș, ca text) și completitudinea dosarului de vehicul (nu există dosar).
    ///
    /// Sunt lăsate aici, la zero, ca prezența lor în spec să rămână vizibilă și ca activarea lor
    /// să fie o valoare de configurare, nu o modificare de cod. Consecința asumată: maximul
    /// atingibil azi e 80, nu 100 — deliberat **nu** rescalăm, fiindcă scorurile de acum ar
    /// deveni incomparabile cu cele de după.
    /// </summary>
    public int MapPin { get; set; }
    public int VehicleDossier { get; set; }
}
