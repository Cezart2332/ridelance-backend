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
    string? DefaultConditions,
    /// <summary>Ziua de plată a chiriei: <c>Monday</c> … <c>Sunday</c> (clauza 3.2).</summary>
    string? PaymentDueDay = null,
    /// <summary><c>Cash</c>, <c>BankTransfer</c>, <c>Card</c> sau <c>Other</c> (clauza 3.2).</summary>
    string? PaymentMethod = null,
    /// <summary>Penalitate de întârziere, % pe zi (clauza 3.5). Gol sau 0 = fără penalități.</summary>
    decimal? LatePenaltyPercentPerDay = null,
    /// <summary>Franșiza la daună, în bani (clauza 6.2). 0 = fără franșiză.</summary>
    long? FranchiseBani = null,
    /// <summary>Preavizul de denunțare, în zile (clauza 7.1).</summary>
    int? NoticeDays = null,
    /// <summary><c>NotDue</c>, <c>Proportional</c> sau <c>FullyDue</c> (clauza 5.4).</summary>
    string? UnavailabilityRule = null);
