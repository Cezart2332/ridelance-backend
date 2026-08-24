namespace Application.Cars;

/// <summary>
/// Detaliile de anunț adăugate de fluxul pe șase pași (marketplace + dosar vehicul).
/// </summary>
/// <remarks>
/// Grupate într-un singur record fiindcă intră identic în creare, în editare și în DTO. Trei
/// liste paralele de câmpuri ar fi divergat la prima adăugare făcută în grabă doar în două
/// dintre ele.
///
/// Toate sunt opționale: principiul fluxului e că mașina se publică fără dosar complet.
/// </remarks>
public sealed record CarListingDetails(
    string? Zone = null,
    double? Latitude = null,
    double? Longitude = null,
    bool ShowExactLocation = false,
    bool UseCompanyContacts = true,
    string? Color = null,
    int? Seats = null,
    string? MinimumPeriod = null,
    string? Conditions = null,
    DateTime? AvailableFromUtc = null,
    string? PlateNumber = null,
    string? Vin = null,
    int? Mileage = null,
    DateTime? FirstRegistrationAtUtc = null);
