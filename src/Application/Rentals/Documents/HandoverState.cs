using Domain.Rentals;

namespace Application.Rentals.Documents;

/// <summary>
/// Ce se tipărește pe un proces-verbal: starea mașinii într-un moment, predare sau primire.
/// </summary>
/// <remarks>
/// Două surse, în ordinea asta: predarea făcută propriu-zis (<see cref="CheckRecord"/>, cu
/// fotografii și ora reală) și, până există ea, ce s-a notat pe închiriere când s-a creat. Fără
/// prima, procesul-verbal din contract ar fi ignorat tot ce s-a consemnat la predare.
/// </remarks>
internal sealed record HandoverState(
    DateTime DateUtc,
    int? Mileage,
    string? Fuel,
    int? Keys,
    IReadOnlyList<string> Documents,
    string? DocumentsOther,
    IReadOnlyList<string> Accessories,
    string? Damages,
    string? Notes,
    /// <summary><c>null</c> = nu se știe încă (nu s-a făcut predarea în aplicație).</summary>
    bool? Photos)
{
    private const string KeysAccessory = "Chei";

    /// <summary>La predare: din predarea consemnată, altfel din închiriere.</summary>
    public static HandoverState Pickup(Rental rental, CheckRecord? checkIn)
    {
        ArgumentNullException.ThrowIfNull(rental);

        IEnumerable<string> rentalAccessories = rental.Accessories
            .Concat(string.IsNullOrWhiteSpace(rental.AccessoriesOther) ? [] : [rental.AccessoriesOther.Trim()]);

        IEnumerable<string> accessories = checkIn is { Accessories.Count: > 0 } ? checkIn.Accessories : rentalAccessories;

        return new HandoverState(
            checkIn?.OccurredAtUtc ?? rental.StartAtUtc,
            checkIn?.Mileage ?? rental.StartMileage,
            Text(checkIn?.FuelLevel) ?? Text(rental.FuelLevelAtPickup),
            KeysAtPickup(rental, checkIn),
            checkIn is { Documents.Count: > 0 } ? checkIn.Documents : rental.HandoverDocuments,
            Text(checkIn?.DocumentsOther) ?? Text(rental.HandoverDocumentsOther),
            WithoutKeys(accessories),
            Text(checkIn?.Damages) ?? Text(rental.DamagesAtPickup),
            Text(checkIn?.Notes) ?? Text(rental.Notes),
            checkIn is null ? null : checkIn.Photos.Count > 0);
    }

    /// <summary>La primire: doar din primirea consemnată. Fără ea, procesul-verbal se completează de mână.</summary>
    public static HandoverState Return(Rental rental, CheckRecord? checkOut)
    {
        ArgumentNullException.ThrowIfNull(rental);

        if (checkOut is null)
        {
            return new HandoverState(rental.ClosedAtUtc ?? rental.EndAtUtc, null, null, null, [], null, [], null, null, null);
        }

        return new HandoverState(
            checkOut.OccurredAtUtc,
            checkOut.Mileage,
            Text(checkOut.FuelLevel),
            checkOut.KeysCount ?? (HasKeys(checkOut.Accessories) ? 1 : null),
            checkOut.Documents,
            Text(checkOut.DocumentsOther),
            WithoutKeys(checkOut.Accessories),
            Text(checkOut.Damages),
            Text(checkOut.Notes),
            checkOut.Photos.Count > 0);
    }

    /// <summary>
    /// Seturile de chei predate. Înainte de câmpul dedicat se știa doar dacă s-au predat („Chei”
    /// în lista de accesorii), adică cel puțin un set.
    /// </summary>
    public static int? KeysAtPickup(Rental rental, CheckRecord? checkIn)
    {
        ArgumentNullException.ThrowIfNull(rental);

        return checkIn?.KeysCount
            ?? rental.KeysCount
            ?? (HasKeys(checkIn?.Accessories) || HasKeys(rental.Accessories) ? 1 : null);
    }

    private static bool HasKeys(IEnumerable<string>? accessories) =>
        accessories?.Any(a => a.Trim().Equals(KeysAccessory, StringComparison.OrdinalIgnoreCase)) == true;

    private static List<string> WithoutKeys(IEnumerable<string> accessories) =>
        accessories
            .Where(a => !string.IsNullOrWhiteSpace(a) && !a.Trim().Equals(KeysAccessory, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Trim())
            .ToList();

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
