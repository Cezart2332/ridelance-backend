using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Queries.GetRentals;

public sealed record GetRentalsQuery : IQuery<RentalOverviewDto>;

internal sealed class GetRentalsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetRentalsQuery, RentalOverviewDto>
{
    /// <summary>Săptămâni într-o lună, pentru valoarea contractuală lunară. 52/12.</summary>
    private const decimal WeeksPerMonth = 52m / 12m;

    public async Task<Result<RentalOverviewDto>> Handle(
        GetRentalsQuery query,
        CancellationToken cancellationToken)
    {
        List<Rental> rentals = await context.Rentals
            .AsNoTracking()
            .Where(r => r.OwnerUserId == userContext.UserId)
            .OrderByDescending(r => r.StartAtUtc)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var carIds = rentals.Select(r => r.CarId).Distinct().ToList();
        Dictionary<Guid, string> labels = carIds.Count == 0
            ? []
            : await context.Cars
                .AsNoTracking()
                .Where(c => carIds.Contains(c.Id))
                .Select(c => new { c.Id, Label = c.Brand + " " + c.Model + ", " + c.Year })
                .ToDictionaryAsync(x => x.Id, x => x.Label, cancellationToken);

        DateTime now = DateTime.UtcNow;

        var dtos = rentals
            .Select(r => Map(r, labels.GetValueOrDefault(r.CarId, "Mașină ștearsă"), now))
            .ToList();

        int fleetSize = await context.Cars
            .AsNoTracking()
            .CountAsync(c => c.PostedByUserId == userContext.UserId, cancellationToken);

        var open = dtos
            .Where(d => d.Status != RentalStatus.Completed && d.Status != RentalStatus.Upcoming)
            .ToList();

        var summary = new RentalSummaryDto(
            open.Count,
            // Valoarea lunară a contractelor deschise, la tariful lor propriu.
            (long)open.Sum(d => d.WeeklyRentBani * WeeksPerMonth),
            dtos.Count(d => d.Status == RentalStatus.EndingSoon),
            // Mașini fără nicio închiriere deschisă. O mașină nu poate fi la doi chiriași deodată.
            Math.Max(0, fleetSize - open.Select(d => d.CarId).Distinct().Count()));

        return Result.Success(new RentalOverviewDto(summary, dtos));
    }

    private static RentalDto Map(Rental rental, string carLabel, DateTime nowUtc)
    {
        // Valoarea contractuală: chiria săptămânală × durata convenită, nu × cât a durat efectiv.
        decimal weeks = (decimal)(rental.EndAtUtc - rental.StartAtUtc).TotalDays / 7m;
        long contractValue = (long)Math.Round(rental.WeeklyRentBani * Math.Max(weeks, 0m));

        return new RentalDto(
            rental.Id,
            rental.CarId,
            carLabel,
            rental.TenantName,
            rental.TenantType.ToString(),
            rental.TenantFiscalCode,
            rental.TenantPhone,
            rental.TenantEmail,
            rental.StartAtUtc,
            rental.EndAtUtc,
            rental.ClosedAtUtc,
            rental.WeeklyRentBani,
            rental.DepositBani,
            rental.HasKmLimit,
            rental.ExtraKmCostBani,
            rental.FuelRule,
            rental.StartMileage,
            rental.Accessories,
            rental.Notes,
            RentalStatus.For(rental, nowUtc),
            contractValue);
    }
}
