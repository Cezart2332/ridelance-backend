namespace Application.Rentals;

/// <summary>
/// Valorile cu care se completează singur formularul de închiriere.
/// </summary>
/// <remarks>
/// Toate opționale: o flotă care n-a apucat să și le seteze primește un formular gol, nu unul plin
/// de zerouri care par convenite.
/// </remarks>
public sealed record RentalDefaultsDto(
    long? WeeklyRentBani,
    long? DepositBani,
    int? MinPeriodDays,
    bool HasKmLimit,
    int? MileageLimit,
    long? ExtraKmCostBani,
    string? FuelRule,
    string? DefaultConditions);
