using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>Modul de deținere a vehiculului declarat de client la Pasul 5.</summary>
public enum VehicleOwnershipMode
{
    /// <summary>Am mașină (proprietate personală).</summary>
    Owned = 0,
    /// <summary>Închiriez mașina.</summary>
    Rented = 1,
    /// <summary>Mașina e în leasing.</summary>
    Leased = 2,
    /// <summary>Mașina e deținută în comodat.</summary>
    Comodat = 3,
    /// <summary>Adaug mașina mai târziu — frunza devine „nu se aplică” temporar.</summary>
    AddedLater = 4,
}

/// <summary>Ciclul de viață al vehiculului în onboarding.</summary>
public enum PfaVehicleStatus
{
    Draft = 0,
    /// <summary>Vehiculul e declarat, se așteaptă documentele și copia conformă.</summary>
    DocumentsPending = 1,
    /// <summary>Vehiculul e complet (copie conformă + ecusoane emise).</summary>
    Active = 2,
}

/// <summary>
/// Pasul 5 — vehiculul clientului. NU se confundă cu <see cref="Domain.Cars.Car"/> (anunțul din
/// marketplace); legătura opțională se face prin <see cref="MarketplaceCarId"/>. Ține datele
/// declarate ale mașinii; documentele (talon, RCA, contract) rămân uploadurile existente.
/// </summary>
public sealed class PfaVehicle : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    public VehicleOwnershipMode OwnershipMode { get; set; } = VehicleOwnershipMode.Owned;
    /// <summary>Clientul amână adăugarea vehiculului (frunza devine „nu se aplică”).</summary>
    public bool AddLater { get; set; }

    /// <summary>Numărul de înmatriculare (folosit în numele dosarului).</summary>
    public string? PlateNumber { get; set; }
    public string? Vin { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? FirstRegistrationYear { get; set; }

    /// <summary>Legătura opțională către anunțul din marketplace (Domain/Cars/Car).</summary>
    public Guid? MarketplaceCarId { get; set; }

    public PfaVehicleStatus Status { get; set; } = PfaVehicleStatus.Draft;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
    public VehicleCopyRequest? CopyRequest { get; set; }
    public List<VehicleBadge> Badges { get; set; } = [];
}
