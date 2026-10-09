using SharedKernel;

namespace Domain.PfaRegistrations.ArrFleet;

/// <summary>
/// Pasul „ARR &amp; Cont Flotă”: clientul încarcă actele, alege platformele și modul de deținere a
/// mașinii, plătește și trimite. Restul (contul ARR, conturile de flotă, autorizația, copia
/// conformă, ecusoanele) îl face agentul RIDElance din admin, pe aceeași entitate.
///
/// Conturile de șofer stau în <see cref="PfaPlatformAccount"/>, nu într-o copie: contabilitatea și
/// dashboardul citesc deja platformele de acolo.
/// </summary>
public sealed class ArrFleetApplication : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public Guid UserId { get; set; }

    public ArrFleetStatus Status { get; set; } = ArrFleetStatus.Draft;
    public ArrFleetPlatforms Platforms { get; set; }

    /// <summary>Calculată pe server din platforme (<see cref="ArrFleetPricing"/>), niciodată primită.</summary>
    public long PaymentAmountBani { get; set; }

    /// <summary>
    /// Ultima schimbare a sumei. O dovadă de plată încărcată înainte e pentru suma veche, deci
    /// clientul vede un avertisment până încarcă alta.
    /// </summary>
    public DateTime? PaymentAmountChangedAtUtc { get; set; }

    public ArrFleetVehicleOwnership? VehicleOwnership { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    /// <summary>Motivul pentru care adminul a redeschis pasul, arătat clientului.</summary>
    public string? ReopenedReason { get; set; }
    public DateTime? ReopenedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public PfaRegistration PfaRegistration { get; set; } = null!;
    public List<ArrFleetStatusLog> StatusLogs { get; set; } = [];

    public bool Has(ArrFleetPlatforms platform) => (Platforms & platform) == platform;

    public int PlatformCount =>
        (Has(ArrFleetPlatforms.Uber) ? 1 : 0) + (Has(ArrFleetPlatforms.Bolt) ? 1 : 0);
}
