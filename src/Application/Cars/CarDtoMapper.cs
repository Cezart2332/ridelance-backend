using Application.Abstractions.Data;
using Application.Cars.Queries.GetAllCars;
using Domain.Cars;
using Domain.Companies;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Application.Cars;

internal static class CarDtoMapper
{
    public static CarStatsDto MapStats(Car car, int viewsLast7Days) =>
        new(car.ViewCount, car.UniqueViewCount, viewsLast7Days, car.ClickCount, car.Leads.Count);

    /// <param name="viewsLast7Days">
    /// Se numără din <c>car_views</c>, deci îl aduce apelantul: pentru o listă e o singură grupare,
    /// nu un query per mașină.
    /// </param>
    public static CarDto ToDto(
        Car car,
        bool postedByAdmin,
        int viewsLast7Days = 0,
        CarOwnerDto? owner = null,
        int? recommendationScore = null,
        List<ScoreSuggestionDto>? scoreSuggestions = null) =>
        new(
            car.Id,
            car.Slug,
            car.Brand,
            car.Model,
            car.Year,
            car.Engine,
            car.Transmission,
            car.Location,
            car.PricePerWeek,
            car.OldPrice,
            car.DiscountActive,
            car.Garantie,
            car.OfferType.ToString(),
            car.Status.ToString(),
            car.UberCategories,
            car.BoltCategories,
            car.Badges,
            car.Description,
            car.Active,
            car.ListingSource.ToString(),
            car.ApprovalStatus.ToString(),
            car.PaymentStatus.ToString(),
            car.PaidAtUtc,
            postedByAdmin,
            owner,
            recommendationScore,
            scoreSuggestions,
            car.Images.OrderBy(i => i.DisplayOrder)
                .Select(i => new CarImageDto(i.Id, i.Url, i.DisplayOrder))
                .ToList(),
            car.CreatedAtUtc,
            MapStats(car, viewsLast7Days));

    /// <summary>
    /// Profilurile proprietarilor pentru o listă de mașini, într-un singur query.
    ///
    /// Anunțurile platformei (postate de admin) rămân fără proprietar în mod deliberat: „RIDElance"
    /// ca proprietar ar fi sugerat un partener printre parteneri.
    /// </summary>
    public static async Task<Dictionary<Guid, CarOwnerDto>> LoadOwnersAsync(
        IApplicationDbContext context,
        IEnumerable<Car> cars,
        IReadOnlyDictionary<Guid, UserRole> posterRoles,
        CancellationToken cancellationToken)
    {
        var ownerIds = cars
            .Where(c => c.PostedByUserId.HasValue && !IsPostedByAdmin(c, posterRoles))
            .Select(c => c.PostedByUserId!.Value)
            .ToHashSet();

        if (ownerIds.Count == 0)
        {
            return [];
        }

        List<CompanyProfile> profiles = await context.CompanyProfiles
            .AsNoTracking()
            .Where(p => ownerIds.Contains(p.UserId))
            .ToListAsync(cancellationToken);

        return profiles.ToDictionary(
            p => p.UserId,
            p => new CarOwnerDto(
                p.UserId,
                p.OwnerType.ToString(),
                p.LegalName,
                p.LogoUrl,
                p.Slug,
                p.IsVerified));
    }

    public static bool IsPostedByAdmin(Car car, IReadOnlyDictionary<Guid, UserRole> posterRoles) =>
        car.PostedByUserId is null
        || posterRoles.TryGetValue(car.PostedByUserId.Value, out UserRole role) && role == UserRole.Admin;
}
