using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Bolt;
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
    decimal Net);

/// <remarks>
/// <c>UberRidesAvailable</c> e mereu <c>false</c> cât timp Uber livrează doar CSV-uri cu
/// totaluri lunare: nu există curse individuale de listat. UI-ul spune asta explicit,
/// în loc să lase impresia unui istoric incomplet.
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

        if (platform == "uber")
        {
            return new PfaRidesPageResponse([], page, pageSize, 0, false);
        }

        TimeZoneInfo timeZone = PfaDashboardPeriod.RomaniaTimeZone();
        (DateTime startUtc, DateTime endUtc) = new PfaDashboardPeriod(query.From, query.To).ToUtcBounds(timeZone);
        Guid userId = userContext.UserId;

        // Filtrarea pe metoda de plată și căutarea pe adrese sunt insensibile la diacritice
        // de casă, ceea ce Postgres nu face nativ pe LIKE. Volumul e de ordinul sutelor de
        // curse pe perioadă, așa că se citește intervalul și se filtrează în memorie —
        // același compromis ca în GetAdminOverviewQuery.
        List<BoltOrder> periodOrders = await context.BoltOrders
            .AsNoTracking()
            .Where(o => o.UserId == userId
                && o.OrderStatus == "finished"
                && o.OrderCreatedTime >= startUtc
                && o.OrderCreatedTime < endUtc)
            .ToListAsync(cancellationToken);

        IEnumerable<BoltOrder> filtered = payment switch
        {
            "cash" => periodOrders.Where(IsCash),
            "card" => periodOrders.Where(o => !IsCash(o)),
            _ => periodOrders
        };

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            string term = query.Query.Trim();
            filtered = filtered.Where(o =>
                o.PickupAddress.Contains(term, StringComparison.OrdinalIgnoreCase)
                || o.DestinationAddress.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var matches = filtered.ToList();

        var items = ApplySort(matches, query.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PfaRidesPageResponse(
            items.Select(Map).ToList(),
            page,
            pageSize,
            matches.Count,
            UberRidesAvailable: false);
    }

    /// <summary>Sortabile: data, distanța, durata, netul. Prefixul „-” înseamnă descrescător.</summary>
    private static IEnumerable<BoltOrder> ApplySort(List<BoltOrder> orders, string? sort)
    {
        bool descending = sort?.StartsWith('-') ?? true;
        string field = (sort ?? "-date").TrimStart('-', '+').ToUpperInvariant();

        return field switch
        {
            "DISTANCE" => Order(orders, o => o.RideDistance, descending),
            "DURATION" => Order(orders, DurationMinutes, descending),
            "NET" => Order(orders, o => (double)o.NetEarnings, descending),
            _ => Order(orders, o => (double)o.OrderCreatedTime.Ticks, descending)
        };

        static IEnumerable<BoltOrder> Order(
            List<BoltOrder> source,
            Func<BoltOrder, double> key,
            bool descending) =>
            descending ? source.OrderByDescending(key) : source.OrderBy(key);
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
            order.PaymentMethod.Contains("cash", StringComparison.OrdinalIgnoreCase) ? "cash" : "card",
            order.NetEarnings);
    }
}
