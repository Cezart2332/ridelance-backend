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

    public Guid CarId { get; set; }
    public Guid OwnerUserId { get; set; }

    public string TenantName { get; set; } = string.Empty;
    public TenantType TenantType { get; set; } = TenantType.Individual;

    /// <summary>CNP pentru persoană fizică, CUI pentru PFA sau SRL.</summary>
    public string? TenantFiscalCode { get; set; }
    public string? TenantPhone { get; set; }
    public string? TenantEmail { get; set; }

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

    public bool HasKmLimit { get; set; }
    public long ExtraKmCostBani { get; set; }
    public string? FuelRule { get; set; }

    /// <summary>Kilometrajul la predare, punctul de plecare pentru orice decont de km.</summary>
    public int? StartMileage { get; set; }

    public string? Accessories { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
