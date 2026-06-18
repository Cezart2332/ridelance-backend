using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Bolt;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Bolt.Queries;

public sealed record BoltDashboardPointResponse(
    string Label,
    decimal NetEarnings,
    int OrdersCount,
    decimal Tips,
    decimal Commissions,
    double RideHours);

public sealed record BoltDashboardRideResponse(
    Guid Id,
    DateTime OrderCreatedTime,
    DateTime? OrderFinishedTime,
    string PickupAddress,
    string DestinationAddress,
    double RideDistanceKm,
    decimal RidePrice,
    decimal NetEarnings,
    decimal Tip,
    decimal Commission,
    string PaymentMethod,
    string VehicleModel,
    string VehicleLicensePlate,
    double RideHours);

public sealed record BoltDashboardResponse(
    bool IsConfigured,
    bool IsConnected,
    DateTime? LastFetchedAtUtc,
    string? ErrorMessage,
    string Period,
    int? Year,
    int? Month,
    int TotalOrdersCount,
    decimal TotalNetEarnings,
    decimal TotalTips,
    decimal TotalCommissions,
    double TotalRideDistanceKm,
    double TotalRideHours,
    decimal AverageNetPerRide,
    decimal AverageNetPerRideHour,
    List<BoltDashboardPointResponse> Series,
    List<BoltDashboardRideResponse> RecentRides);

public sealed record GetBoltDashboardQuery(
    string? Period = null,
    int? Year = null,
    int? Month = null) : IQuery<BoltDashboardResponse>;

internal sealed class GetBoltDashboardQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext) : IQueryHandler<GetBoltDashboardQuery, BoltDashboardResponse>
{
    private const string MonthPeriod = "month";
    private const string YearPeriod = "year";
    private const string TotalPeriod = "total";
    private static readonly CultureInfo RomanianCulture = CultureInfo.GetCultureInfo("ro-RO");

    public async Task<Result<BoltDashboardResponse>> Handle(
        GetBoltDashboardQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;
        string period = NormalizePeriod(query.Period);
        TimeZoneInfo romaniaTimeZone = GetRomaniaTimeZone();
        DateTime romaniaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, romaniaTimeZone);

        int? selectedYear = period is MonthPeriod or YearPeriod
            ? query.Year ?? romaniaNow.Year
            : null;
        int? selectedMonth = period == MonthPeriod
            ? query.Month ?? romaniaNow.Month
            : null;

        if (selectedMonth is < 1 or > 12)
        {
            return Result.Failure<BoltDashboardResponse>(
                Error.Problem("Bolt.InvalidMonth", "Luna pentru dashboardul Bolt trebuie să fie între 1 și 12."));
        }

        BoltIntegration? integration = await context.BoltIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(bi => bi.UserId == userId, cancellationToken);

        if (integration is null)
        {
            return EmptyResponse(
                isConfigured: false,
                isConnected: false,
                lastFetchedAtUtc: null,
                errorMessage: null,
                period,
                selectedYear,
                selectedMonth);
        }

        IQueryable<BoltOrder> ordersQuery = context.BoltOrders
            .AsNoTracking()
            .Where(o => o.UserId == userId && o.OrderStatus == "finished");

        (DateTime? startUtc, DateTime? endUtc) = GetUtcBounds(period, selectedYear, selectedMonth, romaniaTimeZone);
        if (startUtc.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.OrderCreatedTime >= startUtc.Value);
        }

        if (endUtc.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.OrderCreatedTime < endUtc.Value);
        }

        List<BoltOrder> orders = await ordersQuery
            .OrderByDescending(o => o.OrderCreatedTime)
            .ToListAsync(cancellationToken);

        int totalOrders = orders.Count;
        decimal totalNet = orders.Sum(o => o.NetEarnings);
        decimal totalTips = orders.Sum(o => o.Tip);
        decimal totalCommissions = orders.Sum(o => o.Commission);
        double totalDistanceKm = orders.Sum(o => o.RideDistance) / 1000.0;
        double totalRideHours = orders.Sum(GetRideHours);
        decimal averageNetPerRide = totalOrders > 0 ? totalNet / totalOrders : 0m;
        decimal averageNetPerRideHour = totalRideHours > 0
            ? totalNet / (decimal)totalRideHours
            : 0m;

        List<BoltDashboardPointResponse> series = BuildSeries(orders, period, selectedYear, selectedMonth, romaniaTimeZone);
        var recentRides = orders
            .Take(6)
            .Select(o => new BoltDashboardRideResponse(
                o.Id,
                o.OrderCreatedTime,
                o.OrderFinishedTime,
                o.PickupAddress,
                o.DestinationAddress,
                Math.Round(o.RideDistance / 1000.0, 1),
                o.RidePrice,
                o.NetEarnings,
                o.Tip,
                o.Commission,
                o.PaymentMethod,
                o.VehicleModel,
                o.VehicleLicensePlate,
                Math.Round(GetRideHours(o), 2)))
            .ToList();

        return new BoltDashboardResponse(
            true,
            integration.IsConnected,
            integration.LastFetchedAtUtc,
            integration.ErrorMessage,
            period,
            selectedYear,
            selectedMonth,
            totalOrders,
            totalNet,
            totalTips,
            totalCommissions,
            Math.Round(totalDistanceKm, 1),
            Math.Round(totalRideHours, 2),
            averageNetPerRide,
            averageNetPerRideHour,
            series,
            recentRides);
    }

    private static Result<BoltDashboardResponse> EmptyResponse(
        bool isConfigured,
        bool isConnected,
        DateTime? lastFetchedAtUtc,
        string? errorMessage,
        string period,
        int? year,
        int? month) =>
        new BoltDashboardResponse(
            isConfigured,
            isConnected,
            lastFetchedAtUtc,
            errorMessage,
            period,
            year,
            month,
            0,
            0m,
            0m,
            0m,
            0,
            0,
            0m,
            0m,
            [],
            []);

    private static string NormalizePeriod(string? period) =>
        period?.Trim().ToUpperInvariant() switch
        {
            "YEAR" => YearPeriod,
            "TOTAL" => TotalPeriod,
            _ => MonthPeriod
        };

    private static (DateTime? StartUtc, DateTime? EndUtc) GetUtcBounds(
        string period,
        int? year,
        int? month,
        TimeZoneInfo timeZone)
    {
        if (period == TotalPeriod)
        {
            return (null, null);
        }

        DateTime startLocal = period == MonthPeriod
            ? new DateTime(year!.Value, month!.Value, 1, 0, 0, 0, DateTimeKind.Unspecified)
            : new DateTime(year!.Value, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        DateTime endLocal = period == MonthPeriod
            ? startLocal.AddMonths(1)
            : startLocal.AddYears(1);

        return (
            TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone),
            TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone));
    }

    private static List<BoltDashboardPointResponse> BuildSeries(
        List<BoltOrder> orders,
        string period,
        int? year,
        int? month,
        TimeZoneInfo timeZone)
    {
        if (period == MonthPeriod)
        {
            int daysInMonth = DateTime.DaysInMonth(year!.Value, month!.Value);
            var groupedByDay = orders
                .GroupBy(o => ToRomaniaTime(o.OrderCreatedTime, timeZone).Day)
                .ToDictionary(g => g.Key, BuildPoint);

            return Enumerable.Range(1, daysInMonth)
                .Select(day => groupedByDay.TryGetValue(day, out BoltDashboardPointResponse? point)
                    ? point with { Label = day.ToString(CultureInfo.InvariantCulture) }
                    : new BoltDashboardPointResponse(day.ToString(CultureInfo.InvariantCulture), 0m, 0, 0m, 0m, 0))
                .ToList();
        }

        if (period == YearPeriod)
        {
            var groupedByMonth = orders
                .GroupBy(o => ToRomaniaTime(o.OrderCreatedTime, timeZone).Month)
                .ToDictionary(g => g.Key, BuildPoint);

            return Enumerable.Range(1, 12)
                .Select(m =>
                {
                    string label = RomanianCulture.DateTimeFormat.AbbreviatedMonthNames[m - 1];
                    return groupedByMonth.TryGetValue(m, out BoltDashboardPointResponse? point)
                        ? point with { Label = label }
                        : new BoltDashboardPointResponse(label, 0m, 0, 0m, 0m, 0);
                })
                .ToList();
        }

        return orders
            .GroupBy(o =>
            {
                DateTime local = ToRomaniaTime(o.OrderCreatedTime, timeZone);
                return new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            })
            .OrderBy(g => g.Key)
            .Select(g => BuildPoint(g) with
            {
                Label = g.Key.ToString("MMM yyyy", RomanianCulture)
            })
            .ToList();
    }

    private static BoltDashboardPointResponse BuildPoint(IEnumerable<BoltOrder> orders)
    {
        var list = orders.ToList();
        return new BoltDashboardPointResponse(
            string.Empty,
            list.Sum(o => o.NetEarnings),
            list.Count,
            list.Sum(o => o.Tip),
            list.Sum(o => o.Commission),
            Math.Round(list.Sum(GetRideHours), 2));
    }

    private static double GetRideHours(BoltOrder order)
    {
        if (!order.OrderFinishedTime.HasValue)
        {
            return 0;
        }

        TimeSpan duration = NormalizeUtc(order.OrderFinishedTime.Value) - NormalizeUtc(order.OrderCreatedTime);
        return duration.TotalHours > 0 ? duration.TotalHours : 0;
    }

    private static DateTime ToRomaniaTime(DateTime utcDate, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(NormalizeUtc(utcDate), timeZone);

    private static DateTime NormalizeUtc(DateTime dateTime) =>
        dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
        };

    private static TimeZoneInfo GetRomaniaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        }
    }
}
