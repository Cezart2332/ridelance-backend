using Domain.Rentals;

namespace Application.Rentals;

/// <param name="Status">
/// Derivat pe server din date și din <c>ClosedAtUtc</c>, nu stocat: o stare stocată ar fi trebuit
/// actualizată de un job la fiecare miezul nopții, iar între rulări ar fi mințit.
/// </param>
public sealed record RentalDto(
    Guid Id,
    string PublicCode,
    Guid CarId,
    string CarLabel,
    TenantDto Tenant,
    string Lifecycle,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    DateTime? ClosedAtUtc,
    long WeeklyRentBani,
    long DepositBani,
    long OtherCostsBani,
    bool HasKmLimit,
    int? MileageLimit,
    long ExtraKmCostBani,
    string? FuelRule,
    string? FuelLevelAtPickup,
    int? StartMileage,
    IReadOnlyList<string> Accessories,
    string? AccessoriesOther,
    string? Notes,
    string Status,
    /// <summary>Valoarea contractuală a perioadei, derivată din chirie și durată.</summary>
    long ContractValueBani,
    // Termenii contractului (clauzele 3.2, 3.5, 5.4, 6.2, 7.1).
    string? PaymentDueDay = null,
    string? PaymentMethod = null,
    decimal? LatePenaltyPercentPerDay = null,
    long? FranchiseBani = null,
    int? NoticeDays = null,
    string? UnavailabilityRule = null,
    // Conducătorul auto suplimentar (clauza 4.3).
    string? AdditionalDriverName = null,
    string? AdditionalDriverCnp = null,
    string? AdditionalDriverLicense = null,
    // Predarea (Anexa nr. 1).
    int? KeysCount = null,
    IReadOnlyList<string>? HandoverDocuments = null,
    string? HandoverDocumentsOther = null,
    string? DamagesAtPickup = null);

public sealed record TenantDto(
    Guid Id,
    string Name,
    string Type,
    string? Cnp,
    string? IdSeries,
    string? IdNumber,
    string? Cui,
    string? RegCom,
    string? Address,
    string? Phone,
    string? Email,
    string? DriverLicenseNumber,
    string? RepresentativeName = null);

public sealed record RentalSummaryDto(
    int ActiveCount,
    long MonthlyContractValueBani,
    int UpcomingHandoverCount,
    int AvailableCars);

public sealed record RentalOverviewDto(RentalSummaryDto Summary, List<RentalDto> Rentals);

public static class RentalStatus
{
    public const string Upcoming = "upcoming";
    public const string Active = "active";
    public const string EndingSoon = "ending_soon";
    public const string Completed = "completed";

    /// <summary>Cu câte zile înainte de predare închirierea se marchează „se apropie predarea".</summary>
    public const int EndingSoonDays = 7;

    /// <summary>Anulată înainte să înceapă. Decizie, nu consecință a calendarului.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Pregătită, neconfirmată. Nu blochează mașina.</summary>
    public const string Draft = "draft";

    public static string For(Rental rental, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rental);

        // Deciziile bat calendarul: o închiriere anulată nu devine „activă" pentru că i-a venit data.
        if (rental.Lifecycle == RentalLifecycle.Cancelled)
        {
            return Cancelled;
        }

        if (rental.Lifecycle == RentalLifecycle.Draft)
        {
            return Draft;
        }

        // Comparație pe zile, nu pe momente: perioada se alege dintr-un calendar, fără oră, iar
        // ora ajunge în bază drept prânz UTC. Comparate ca momente, o închiriere creată dimineața
        // pentru azi ar fi ieșit „viitoare" până la 12:00 — adică ar fi dispărut din lista celor
        // active exact în ziua în care a fost făcută. La fel la celălalt capăt: ultima zi de
        // închiriere e o zi întreagă, nu se încheie la prânz.
        if (rental.ClosedAtUtc.HasValue || rental.EndAtUtc.Date < nowUtc.Date)
        {
            return Completed;
        }

        if (rental.StartAtUtc.Date > nowUtc.Date)
        {
            return Upcoming;
        }

        return rental.EndAtUtc <= nowUtc.AddDays(EndingSoonDays) ? EndingSoon : Active;
    }
}
