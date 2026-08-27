using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
using Application.Cars.Scoring;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Queries.GetAllCars;

internal sealed class GetAllCarsQueryHandler(
    IApplicationDbContext context,
    ListingScoreService scoreService)
    : IQueryHandler<GetAllCarsQuery, List<CarDto>>
{
    public async Task<Result<List<CarDto>>> Handle(GetAllCarsQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Car> queryable = context.Cars
            .AsNoTracking()
            .Include(c => c.Images.OrderBy(i => i.DisplayOrder))
            .Include(c => c.Leads);

        if (query.PosterUserId.HasValue)
        {
            queryable = queryable.Where(c => c.PostedByUserId == query.PosterUserId.Value);
        }
        else if (!query.AdminMode)
        {
            queryable = queryable.Where(CarVisibility.IsPublic);
        }

        List<Car> cars = await Order(queryable, query).ToListAsync(cancellationToken);

        var posterIds = cars
            .Where(c => c.PostedByUserId.HasValue)
            .Select(c => c.PostedByUserId!.Value)
            .ToHashSet();

        Dictionary<Guid, UserRole> posterRoles = posterIds.Count == 0
            ? []
            : await context.Users
                .AsNoTracking()
                .Where(u => posterIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Role, cancellationToken);

        // O singură grupare pentru toată lista: un `count` per mașină ar fi zeci de query-uri.
        var carIds = cars.Select(c => c.Id).ToList();
        DateTime since = DateTime.UtcNow.AddDays(-7);

        Dictionary<Guid, int> recentViews = carIds.Count == 0
            ? []
            : await context.CarViews
                .AsNoTracking()
                .Where(v => carIds.Contains(v.CarId) && v.CreatedAtUtc >= since)
                .GroupBy(v => v.CarId)
                .Select(g => new { CarId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CarId, x => x.Count, cancellationToken);

        Dictionary<Guid, CarOwnerDto> owners =
            await CarDtoMapper.LoadOwnersAsync(context, cars, posterRoles, cancellationToken);

        // Scorul brut și sugestiile se dau **doar** proprietarului, pe anunțurile lui (spec §5.2):
        // expuse public, ar fi devenit o clasificare a partenerilor între ei.
        bool ownerView = query.PosterUserId.HasValue;

        var dtos = new List<CarDto>(cars.Count);
        foreach (Car car in cars)
        {
            List<ScoreSuggestionDto>? suggestions = null;
            if (ownerView)
            {
                IReadOnlyList<ScoreSuggestion> raw = await scoreService.GetSuggestionsAsync(car, cancellationToken);
                suggestions = raw.Select(s => new ScoreSuggestionDto(s.Id, s.Label, s.Points)).ToList();
            }

            dtos.Add(CarDtoMapper.ToDto(
                car,
                CarDtoMapper.IsPostedByAdmin(car, posterRoles),
                recentViews.GetValueOrDefault(car.Id),
                car.PostedByUserId.HasValue ? owners.GetValueOrDefault(car.PostedByUserId.Value) : null,
                ownerView ? car.RecommendationScore : null,
                suggestions));
        }

        return dtos;
    }

    /// <summary>
    /// Ordonarea listei (spec §5).
    /// </summary>
    /// <remarks>
    /// Fiecare variantă se termină cu <c>Id</c>: fără un criteriu final unic, două anunțuri egale
    /// pot ieși în ordine diferită la două cereri, iar la paginare asta înseamnă rânduri care sar
    /// sau dispar între pagini.
    ///
    /// La „Recomandate", indisponibilele merg după cele disponibile indiferent de scor (§5.2).
    /// </remarks>
    private static IQueryable<Car> Order(IQueryable<Car> queryable, GetAllCarsQuery query)
    {
        // Listele de administrare și cele proprii rămân pe „cele mai noi": acolo utilizatorul
        // caută ce a atins ultima dată, nu ce e bine poziționat public.
        if (query.AdminMode || query.PosterUserId.HasValue)
        {
            return queryable.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id);
        }

        return query.Sort switch
        {
            "newest" => queryable.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id),
            "price_asc" => queryable.OrderBy(c => c.PricePerWeek).ThenBy(c => c.Id),
            "price_desc" => queryable.OrderByDescending(c => c.PricePerWeek).ThenBy(c => c.Id),
            _ => queryable
                .OrderBy(c => c.Status == CarStatus.Available ? 0 : 1)
                .ThenByDescending(c => c.RecommendationScore)
                .ThenByDescending(c => c.UpdatedAtUtc)
                .ThenBy(c => c.Id),
        };
    }
}
