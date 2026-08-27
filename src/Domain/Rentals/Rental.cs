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

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
