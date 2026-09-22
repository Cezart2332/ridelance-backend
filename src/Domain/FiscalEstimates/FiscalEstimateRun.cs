namespace Domain.FiscalEstimates;

/// <summary>
/// O rulare a motorului de taxe estimate pentru un PFA și un an: din ce revizie de profil, cu ce
/// parametri și pe ce date financiare s-a calculat. Rezultatele stau în <see cref="FiscalCalculation"/>.
/// </summary>
/// <remarks>
/// Când se schimbă ceva ce intră în calcul (profil, date financiare, rezervă, corecturi), rularea
/// curentă devine <see cref="Stale"/>: UI-ul arată „se calculează”, nu cifrele vechi ca actuale,
/// iar jobul de recalculare face o rulare nouă.
/// </remarks>
public sealed class FiscalEstimateRun
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public int TaxYear { get; set; }
    public int ProfileRevision { get; set; }

    /// <summary><c>null</c> când anul nu are parametri (<c>RULE_UNAVAILABLE</c>).</summary>
    public string? RuleVersion { get; set; }

    public Guid FinancialSnapshotId { get; set; }
    public DateOnly AsOf { get; set; }

    /// <summary>Snapshotul financiar folosit, pentru explicația din admin/contabilitate.</summary>
    public string SnapshotJson { get; set; } = "{}";

    /// <summary>Proiecția și presupunerile (săptămâni folosite, medie, săptămâni rămase), plus avertismentele.</summary>
    public string AssumptionsJson { get; set; } = "{}";

    public string MissingInputsJson { get; set; } = "[]";

    /// <summary>Statusul rezervei, care e și statusul general al rulării.</summary>
    public string Status { get; set; } = string.Empty;

    public bool Stale { get; set; }
    public DateTime? StaleSinceUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<FiscalCalculation> Calculations { get; set; } = [];
}

/// <summary>Rezultatul unei componente (CAS, CASS, INCOME_TAX, RESERVE, PLATFORM_TAXES) într-o rulare.</summary>
public sealed class FiscalCalculation
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string Component { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary><c>null</c> când nu se poate calcula. Niciodată 0 inventat.</summary>
    public decimal? Amount { get; set; }

    public string? ReasonCode { get; set; }
    public string MissingInputsJson { get; set; } = "[]";

    /// <summary>Pașii calculului (N, baza, excepții, deduceri), pentru explicație.</summary>
    public string BreakdownJson { get; set; } = "{}";

    public FiscalEstimateRun Run { get; set; } = null!;
}
