namespace Domain.Cars;

/// <summary>
/// Starea mașinii ca bun al flotei. Nu spune nimic despre anunț — aia e
/// <see cref="ListingStatus"/>.
/// </summary>
public enum CarStatus
{
    Available,    // Disponibilă acum
    ComingSoon,   // În curând
    Unavailable,  // Indisponibilă
    InService,    // În service

    /// <summary>Închiriată în acest moment. Se pune și se ridică din fluxul de închiriere.</summary>
    Rented,

    /// <summary>Ieșită din flotă. Rămâne pentru istoric; nu se mai poate închiria.</summary>
    Archived,
}
