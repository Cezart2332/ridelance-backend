using Application.Abstractions.Messaging;

namespace Application.Cars.Queries.GetAllCars;

/// <param name="Sort">
/// Cum se ordonează lista publică. `recommended` e implicitul din spec §5.1; restul valorilor
/// sunt cele din selectorul de sortare. O valoare necunoscută cade pe implicit, nu dă eroare:
/// un query string greșit nu are voie să lase marketplace-ul gol.
/// </param>
public sealed record GetAllCarsQuery(
    bool AdminMode = false,
    Guid? PosterUserId = null,
    string? Sort = null) : IQuery<List<CarDto>>;

public sealed record CarDto(
    Guid Id,
    string Slug,
    string Brand,
    string Model,
    int Year,
    string Engine,
    string Transmission,
    string Location,
    decimal PricePerWeek,
    decimal? OldPrice,
    bool DiscountActive,
    decimal? Garantie,
    string OfferType,
    string Status,
    List<string> UberCategories,
    List<string> BoltCategories,
    List<string> Badges,
    string Description,
    bool Active,
    string ListingSource,
    string ApprovalStatus,
    string PaymentStatus,
    DateTime? PaidAtUtc,
    bool PostedByAdmin,
    CarOwnerDto? Owner,
    int? RecommendationScore,
    List<ScoreSuggestionDto>? ScoreSuggestions,
    List<CarImageDto> Images,
    DateTime CreatedAtUtc,
    CarStatsDto Stats);

#pragma warning disable CA1054
public sealed record CarImageDto(Guid Id, string ImageUrl, int DisplayOrder);

/// <summary>
/// Cine închiriază mașina (spec §4.1).
///
/// `null` cât timp proprietarul nu și-a completat profilul de firmă: cardul nu afișează atunci
/// niciun proprietar, în loc să compună o identitate din email sau din numele contului.
/// Anunțurile RIDElance nu au proprietar — sunt ale platformei.
/// </summary>
public sealed record CarOwnerDto(
    Guid OwnerId,
    string OwnerType,
    string DisplayName,
    string? LogoUrl,
    string Slug,
    bool Verified);
#pragma warning restore CA1054

/// <summary>O acțiune concretă care crește scorul, cu câte puncte aduce.</summary>
public sealed record ScoreSuggestionDto(string Id, string Label, int Points);

public sealed record CarStatsDto(int Views, int UniqueViews, int ViewsLast7Days, int Clicks, int Forms);
