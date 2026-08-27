using SharedKernel;

namespace Domain.Rentals;

/// <summary>
/// Ce completează singur formularul de închiriere pentru o flotă.
/// </summary>
/// <remarks>
/// Valorile de aici se **copiază** în închiriere la creare, nu se citesc la afișare. E singura
/// formă corectă: o închiriere semnată la 1.800 lei pe săptămână trebuie să rămână la 1.800 și
/// după ce firma își ridică tariful standard. Citirea la afișare ar fi rescris retroactiv fiecare
/// contract semnat.
///
/// Consecința inversă e la fel de importantă și e testată explicit: modificarea unei sume într-o
/// închiriere nu are voie să se întoarcă aici.
/// </remarks>
public sealed class FleetRentalDefaults : Entity
{
    public Guid Id { get; set; }

    /// <summary>Flota. Relație unu-la-unu.</summary>
    public Guid OwnerUserId { get; set; }

    public long? WeeklyRentBani { get; set; }
    public long? DepositBani { get; set; }

    /// <summary>Perioada minimă de închiriere, în zile.</summary>
    public int? MinPeriodDays { get; set; }

    public bool HasKmLimit { get; set; }
    public int? MileageLimit { get; set; }
    public long? ExtraKmCostBani { get; set; }

    public string? FuelRule { get; set; }
    public string? DefaultConditions { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
