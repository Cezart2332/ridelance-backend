using Domain.Cars;

namespace Application.Cars;

internal static class CarListingDetailsMapper
{
    /// <summary>
    /// Coordonatele se salvează doar în pereche: o latitudine fără longitudine nu e o locație,
    /// iar jumătate de pin pe hartă ar fi aterizat în golful Guineei.
    /// </summary>
    public static void Apply(Car car, CarListingDetails? details)
    {
        if (details is null)
        {
            return;
        }

        car.Zone = details.Zone?.Trim();
        car.ShowExactLocation = details.ShowExactLocation;
        car.UseCompanyContacts = details.UseCompanyContacts;
        car.Color = details.Color?.Trim();
        car.Seats = details.Seats;
        car.MinimumPeriod = details.MinimumPeriod?.Trim();
        car.Conditions = details.Conditions?.Trim();
        car.AvailableFromUtc = details.AvailableFromUtc;
        car.PlateNumber = details.PlateNumber?.Trim().ToUpperInvariant();
        car.Vin = details.Vin?.Trim().ToUpperInvariant();
        car.Mileage = details.Mileage;
        car.FirstRegistrationAtUtc = details.FirstRegistrationAtUtc;

        bool hasPin = details.Latitude.HasValue && details.Longitude.HasValue;
        car.Latitude = hasPin ? details.Latitude : null;
        car.Longitude = hasPin ? details.Longitude : null;
    }

    public static CarListingDetails From(Car car) => new(
        car.Zone,
        car.Latitude,
        car.Longitude,
        car.ShowExactLocation,
        car.UseCompanyContacts,
        car.Color,
        car.Seats,
        car.MinimumPeriod,
        car.Conditions,
        car.AvailableFromUtc,
        car.PlateNumber,
        car.Vin,
        car.Mileage,
        car.FirstRegistrationAtUtc);
}
