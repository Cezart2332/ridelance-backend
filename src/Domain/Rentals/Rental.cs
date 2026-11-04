using SharedKernel;

namespace Domain.Rentals;

/// <summary>Cui i se închiriază: decide ce identificator fiscal se cere.</summary>
public enum TenantType
{
    Individual = 0,
    Pfa = 1,
    Srl = 2,
}

/// <summary>
/// O închiriere a unei mașini din flotă către un chiriaș.
/// </summary>
/// <remarks>
/// Valorile contractuale se copiază pe închiriere, nu se citesc din setările firmei la afișare:
/// setările se schimbă, iar o închiriere semnată la 1.800 lei/săptămână trebuie să rămână la
/// 1.800 chiar dacă tariful standard crește luna următoare.
/// </remarks>
public sealed class Rental : Entity
{
    public Guid Id { get; set; }

    /// <summary>
    /// Codul din documente și din discuțiile cu clientul: <c>RL-000123</c>. Stabil, secvențial,
    /// niciodată reutilizat. Id-ul e un GUID — nu se poate citi la telefon.
    /// </summary>
    public string PublicCode { get; set; } = string.Empty;

    public Guid CarId { get; set; }
    public Guid OwnerUserId { get; set; }

    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    /// <summary>Ce s-a decis: pregătită, confirmată sau anulată. Restul stărilor sunt derivate.</summary>
    public RentalLifecycle Lifecycle { get; set; } = RentalLifecycle.Confirmed;

    public DateTime StartAtUtc { get; set; }

    /// <summary>Data estimată de predare. Închirierea se poate încheia și mai devreme.</summary>
    public DateTime EndAtUtc { get; set; }

    /// <summary>
    /// Când s-a încheiat efectiv. `null` = încă deschisă. Separat de <see cref="EndAtUtc"/> fiindcă
    /// „până când era planificată" și „când s-a terminat" sunt două întrebări diferite, iar
    /// suprascrierea primei ar fi șters ce s-a convenit inițial.
    /// </summary>
    public DateTime? ClosedAtUtc { get; set; }

    public long WeeklyRentBani { get; set; }
    public long DepositBani { get; set; }

    /// <summary>Costuri în afara chiriei și a garanției, convenite la semnare.</summary>
    public long OtherCostsBani { get; set; }

    public bool HasKmLimit { get; set; }

    /// <summary>Câți kilometri sunt incluși. Lipsea: se putea spune „cu limită" fără să spui care.</summary>
    public int? MileageLimit { get; set; }

    public long ExtraKmCostBani { get; set; }
    public string? FuelRule { get; set; }

    /// <summary>Nivelul la predare, așa cum se citește de pe bord: „plin", „3/4", „80%".</summary>
    public string? FuelLevelAtPickup { get; set; }

    /// <summary>Kilometrajul la predare, punctul de plecare pentru orice decont de km.</summary>
    public int? StartMileage { get; set; }

    /// <summary>
    /// Accesoriile predate, ca listă. Erau un singur șir de text, în care „2 chei" și „doua chei"
    /// erau lucruri diferite, imposibil de numărat la primire.
    /// </summary>
    public List<string> Accessories { get; set; } = [];

    /// <summary>Ce s-a predat în plus față de lista standard.</summary>
    public string? AccessoriesOther { get; set; }

    public string? Notes { get; set; }

    // --- Termenii contractului, copiați din setările flotei la creare ---

    /// <summary>Ziua săptămânii în care se plătește chiria (clauza 3.2).</summary>
    public DayOfWeek? PaymentDueDay { get; set; }

    /// <summary>Cum se plătește chiria (clauza 3.2).</summary>
    public RentalPaymentMethod? PaymentMethod { get; set; }

    /// <summary>Penalitatea de întârziere, % pe zi (clauza 3.5). Gol = nu s-au convenit penalități.</summary>
    public decimal? LatePenaltyPercentPerDay { get; set; }

    /// <summary>Franșiza la daună (clauza 6.2). Zero e o valoare: „fără franșiză”.</summary>
    public long? FranchiseBani { get; set; }

    /// <summary>Preavizul de denunțare, în zile (clauza 7.1).</summary>
    public int? NoticeDays { get; set; }

    /// <summary>Chiria cât mașina e indisponibilă fără vina chiriașului (clauza 5.4).</summary>
    public RentalUnavailabilityRule? UnavailabilityRule { get; set; }

    // --- Conducătorul auto suplimentar (clauza 4.3). Fără nume, conduce doar chiriașul. ---

    public string? AdditionalDriverName { get; set; }
    public string? AdditionalDriverCnp { get; set; }
    public string? AdditionalDriverLicense { get; set; }

    // --- Predarea (Anexa nr. 1), când nu s-a făcut încă predarea propriu-zisă (CheckRecord) ---

    /// <summary>Câte seturi de chei s-au predat.</summary>
    public int? KeysCount { get; set; }

    /// <summary>Documentele predate, ca <see cref="HandoverDocumentCodes"/>.</summary>
    public List<string> HandoverDocuments { get; set; } = [];

    /// <summary>Alte documente predate, în afara talonului și a RCA-ului.</summary>
    public string? HandoverDocumentsOther { get; set; }

    /// <summary>Avariile existente la predare. Gol = rămâne de completat de mână pe hârtie.</summary>
    public string? DamagesAtPickup { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
