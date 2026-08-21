using Domain.Rentals;

namespace Application.Rentals;

/// <param name="Status">
/// Derivat pe server din date și din <c>ClosedAtUtc</c>, nu stocat: o stare stocată ar fi trebuit
/// actualizată de un job la fiecare miezul nopții, iar între rulări ar fi mințit.
/// </param>
public sealed record RentalDto(
    Guid Id,
    Guid CarId,
    string CarLabel,
    string TenantName,
    string TenantType,
    string? TenantFiscalCode,
    string? TenantPhone,
    string? TenantEmail,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    DateTime? ClosedAtUtc,
    long WeeklyRentBani,
    long DepositBani,
    bool HasKmLimit,
    long ExtraKmCostBani,
    string? FuelRule,
    int? StartMileage,
    string? Accessories,
    string? Notes,
    string Status,
    /// <summary>Valoarea contractuală a perioadei, derivată din chirie și durată.</summary>
    long ContractValueBani);

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

    public static string For(Rental rental, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rental);

        if (rental.ClosedAtUtc.HasValue || rental.EndAtUtc < nowUtc)
        {
            return Completed;
        }

        if (rental.StartAtUtc > nowUtc)
        {
            return Upcoming;
        }

        return rental.EndAtUtc <= nowUtc.AddDays(EndingSoonDays) ? EndingSoon : Active;
    }
}
