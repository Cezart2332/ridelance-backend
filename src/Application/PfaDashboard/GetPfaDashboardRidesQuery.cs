using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Bolt;
using Domain.Uber;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaDashboard;

public sealed record PfaRideResponse(
    Guid Id,
    string Platform,
    DateTime StartedAtUtc,
    string? Category,
    string? Pickup,
    string? Dropoff,
    double? DistanceKm,
    double? DurationMin,
    string PaymentType,
    decimal? Net);

/// <remarks>
/// Cursele Uber vin din raportul de curse importat. Raportul nu are câștigul pe cursă, deci
/// <c>Net</c> e <c>null</c> pe ele. <c>UberRidesAvailable</c> spune dacă PFA-ul are măcar o
/// cursă Uber salvată; fără ea, UI-ul explică de ce lista are doar Bolt.
/// </remarks>
public sealed record PfaRidesPageResponse(
    List<PfaRideResponse> Items,
    int Page,
    int PageSize,
    int Total,
    bool UberRidesAvailable);

public sealed record GetPfaDashboardRidesQuery(
    DateOnly From,
    DateOnly To,
    string? Platform,
    string? Payment,
    int Page,
    int PageSize,
    string? Sort,
    string? Query) : IQuery<PfaRidesPageResponse>;

internal sealed class GetPfaDashboardRidesQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetPfaDashboardRidesQuery, PfaRidesPageResponse>
{
    private const int MaxPageSize = 100;

    public async Task<Result<PfaRidesPageResponse>> Handle(
        GetPfaDashboardRidesQuery query,
        CancellationToken cancellationToken)
    {
        if (query.To < query.From)
        {
            return Result.Failure<PfaRidesPageResponse>(
                Error.Problem("PfaDashboard.InvalidRange", "Sfârșitul perioadei nu poate fi înaintea începutului."));
        }

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        string platform = GetPfaDashboardSummaryQueryHandler.NormalizePlatform(query.Platform);
        string payment = GetPfaDashboardSummaryQueryHandler.NormalizePayment(query.Payment);

        TimeZoneInfo timeZone = PfaDashboardPeriod.RomaniaTimeZone();
        (DateTime startUtc, DateTime endUtc) = new PfaDashboardPeriod(query.From, query.To).ToUtcBounds(timeZone);
        Guid userId = userContext.UserId;

        // Filtrarea pe metoda de plată și căutarea pe adrese sunt insensibile la diacritice
        // de casă, ceea ce Postgres nu face nativ pe LIKE. Volumul e de ordinul sutelor de
        // curse pe perioadă, așa că se citește intervalul și se filtrează în memorie —
        // același compromis ca în GetAdminOverviewQuery.
        List<PfaRideResponse> rides = [];

        if (platform != "uber")
        {
            List<BoltOrder> boltOrders = await context.BoltOrders
                .AsNoTracking()
                .Where(o => o.UserId == userId
                    && o.OrderStatus == "finished"
                    && o.OrderCreatedTime >= startUtc
                    && o.OrderCreatedTime < endUtc)
                .ToListAsync(cancellationToken);

            rides.AddRange(boltOrders.Select(Map));
        }

        if (platform != "bolt")
        {
            List<UberTrip> uberTrips = await context.UberTrips
                .AsNoTracking()
                .Where(t => t.UserId == userId
                    && t.Status == "completed"
                    && t.RequestedAtUtc >= startUtc
                    && t.RequestedAtUtc < endUtc)
                .ToListAsync(cancellationToken);

            rides.AddRange(uberTrips.Select(Map));
        }

        IEnumerable<PfaRideResponse> filtered = payment switch
        {
            "cash" => rides.Where(r => r.PaymentType == "cash"),
            "card" => rides.Where(r => r.PaymentType != "cash"),
            _ => rides
        };

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            string term = query.Query.Trim();
            filtered = filtered.Where(r =>
                (r.Pickup?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Dropoff?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var matches = filtered.ToList();

        var items = ApplySort(matches, query.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        bool uberRidesAvailable = await context.UberTrips.AnyAsync(t => t.UserId == userId, cancellationToken);

        return new PfaRidesPageResponse(
            items,
            page,
            pageSize,
            matches.Count,
            uberRidesAvailable);
    }

    /// <summary>
    /// Sortabile: data, distanța, durata, netul. Prefixul „-” înseamnă descrescător. Valorile
    /// lipsă (netul curselor Uber, o durată necunoscută) stau la coadă în ambele sensuri.
    /// </summary>
    private static IEnumerable<PfaRideResponse> ApplySort(List<PfaRideResponse> rides, string? sort)
    {
        bool descending = sort?.StartsWith('-') ?? true;
        string field = (sort ?? "-date").TrimStart('-', '+').ToUpperInvariant();

        return field switch
        {
            "DISTANCE" => Order(rides, r => r.DistanceKm, descending),
            "DURATION" => Order(rides, r => r.DurationMin, descending),
            "NET" => Order(rides, r => (double?)r.Net, descending),
            _ => Order(rides, r => r.StartedAtUtc.Ticks, descending)
        };

        static IEnumerable<PfaRideResponse> Order(
            List<PfaRideResponse> source,
            Func<PfaRideResponse, double?> key,
            bool descending)
        {
            IOrderedEnumerable<PfaRideResponse> known = source.OrderBy(r => key(r) is null);
            return descending ? known.ThenByDescending(key) : known.ThenBy(key);
        }
    }

    private static bool IsCash(BoltOrder order) =>
        order.PaymentMethod.Contains("cash", StringComparison.OrdinalIgnoreCase);

    private static double DurationMinutes(BoltOrder order)
    {
        if (!order.OrderFinishedTime.HasValue)
        {
            return 0;
        }

        double minutes = (PfaDashboardPeriod.NormalizeUtc(order.OrderFinishedTime.Value)
            - PfaDashboardPeriod.NormalizeUtc(order.OrderCreatedTime)).TotalMinutes;

        return minutes > 0 ? minutes : 0;
    }

    private static PfaRideResponse Map(BoltOrder order)
    {
        double minutes = DurationMinutes(order);
        double? durationMin = minutes > 0 ? Math.Round(minutes) : null;

        return new PfaRideResponse(
            order.Id,
            "bolt",
            PfaDashboardPeriod.NormalizeUtc(order.OrderCreatedTime),
            string.IsNullOrWhiteSpace(order.VehicleModel) ? null : order.VehicleModel,
            string.IsNullOrWhiteSpace(order.PickupAddress) ? null : order.PickupAddress,
            string.IsNullOrWhiteSpace(order.DestinationAddress) ? null : order.DestinationAddress,
            order.RideDistance > 0 ? Math.Round(order.RideDistance / 1000.0, 1) : null,
            durationMin,
            IsCash(order) ? "cash" : "card",
            order.NetEarnings);
    }

    /// <summary>
    /// O cursă Uber. Tipul plății vine ca <c>cash</c>, <c>braintree</c>, <c>apple_pay</c> etc. —
    /// tot ce nu e numerar e card. Netul lipsește: Uber îl raportează doar pe lună.
    /// </summary>
    private static PfaRideResponse Map(UberTrip trip)
    {
        double? durationMin = null;
        if (trip.DroppedOffAtUtc is DateTime droppedOff)
        {
            double minutes = (PfaDashboardPeriod.NormalizeUtc(droppedOff) - PfaDashboardPeriod.NormalizeUtc(trip.RequestedAtUtc)).TotalMinutes;
            durationMin = minutes > 0 ? Math.Round(minutes) : null;
        }

        return new PfaRideResponse(
            trip.Id,
            "uber",
            PfaDashboardPeriod.NormalizeUtc(trip.RequestedAtUtc),
            string.IsNullOrWhiteSpace(trip.ProductType) ? null : trip.ProductType,
            string.IsNullOrWhiteSpace(trip.PickupAddress) ? null : trip.PickupAddress,
            string.IsNullOrWhiteSpace(trip.DestinationAddress) ? null : trip.DestinationAddress,
            trip.DistanceKm > 0 ? Math.Round(trip.DistanceKm, 1) : null,
            durationMin,
            trip.PaymentType.Equals("cash", StringComparison.OrdinalIgnoreCase) ? "cash" : "card",
            Net: null);
    }
}
