using Application.Cars.Queries.GetAllCars;
using Domain.Cars;
using Domain.Users;

namespace Application.Cars;

internal static class CarDtoMapper
{
    public static CarStatsDto MapStats(Car car, int viewsLast7Days) =>
        new(car.ViewCount, car.UniqueViewCount, viewsLast7Days, car.ClickCount, car.Leads.Count);

    /// <param name="viewsLast7Days">
    /// Se numără din <c>car_views</c>, deci îl aduce apelantul: pentru o listă e o singură grupare,
    /// nu un query per mașină.
    /// </param>
    public static CarDto ToDto(Car car, bool postedByAdmin, int viewsLast7Days = 0) =>
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
            car.Images.OrderBy(i => i.DisplayOrder)
                .Select(i => new CarImageDto(i.Id, i.Url, i.DisplayOrder))
                .ToList(),
            car.CreatedAtUtc,
            MapStats(car, viewsLast7Days));

    public static bool IsPostedByAdmin(Car car, IReadOnlyDictionary<Guid, UserRole> posterRoles) =>
        car.PostedByUserId is null
        || posterRoles.TryGetValue(car.PostedByUserId.Value, out UserRole role) && role == UserRole.Admin;
}
