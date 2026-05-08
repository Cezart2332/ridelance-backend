using Application.Abstractions.Messaging;

namespace Application.Cars.Queries.GetAllCars;

public sealed record GetAllCarsQuery(bool AdminMode = false) : IQuery<List<CarDto>>;

public sealed record CarDto(
    Guid Id,
    string Brand,
    string Model,
    int Year,
    string Engine,
    string Transmission,
    string Location,
    decimal PricePerWeek,
    decimal? OldPrice,
    bool DiscountActive,
    string OfferType,
    string Status,
    List<string> UberCategories,
    List<string> BoltCategories,
    List<string> Badges,
    string Description,
    bool Active,
    List<CarImageDto> Images,
    DateTime CreatedAtUtc,
    CarStatsDto Stats);

#pragma warning disable CA1054
public sealed record CarImageDto(Guid Id, string ImageUrl, int DisplayOrder);
#pragma warning restore CA1054

public sealed record CarStatsDto(int Views, int Clicks, int Forms);
