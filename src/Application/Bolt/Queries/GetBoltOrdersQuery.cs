using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Bolt;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Bolt.Queries;

public sealed record BoltOrderResponse(
    Guid Id,
    string OrderReference,
    string DriverName,
    string? DriverPhone,
    string PaymentMethod,
    DateTime OrderCreatedTime,
    string OrderStatus,
    string PickupAddress,
    string DestinationAddress,
    double RideDistance,
    decimal RidePrice,
    decimal NetEarnings,
    decimal Tip,
    decimal Commission,
    string VehicleModel,
    string VehicleLicensePlate,
    DateTime? OrderFinishedTime);

public sealed record BoltOrdersResponse(
    List<BoltOrderResponse> Orders,
    int TotalOrdersCount,
    decimal TotalNetEarnings,
    decimal TotalCommissions,
    decimal TotalTips);

public sealed record GetBoltOrdersQuery(int? Limit = null, int? Offset = null) : IQuery<BoltOrdersResponse>;

internal sealed class GetBoltOrdersQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext) : IQueryHandler<GetBoltOrdersQuery, BoltOrdersResponse>
{
    public async Task<Result<BoltOrdersResponse>> Handle(
        GetBoltOrdersQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        IQueryable<BoltOrder> queryable = context.BoltOrders
            .AsNoTracking()
            .Where(o => o.UserId == userId);

        // Compute aggregates efficiently on the database side
        int totalOrdersCount = await queryable.CountAsync(cancellationToken);
        decimal totalNetEarnings = totalOrdersCount > 0 ? await queryable.SumAsync(o => o.NetEarnings, cancellationToken) : 0;
        decimal totalCommissions = totalOrdersCount > 0 ? await queryable.SumAsync(o => o.Commission, cancellationToken) : 0;
        decimal totalTips = totalOrdersCount > 0 ? await queryable.SumAsync(o => o.Tip, cancellationToken) : 0;

        // Fetch paginated subset of orders sorted by newest first
        IQueryable<BoltOrder> ordersQuery = queryable.OrderByDescending(o => o.OrderCreatedTime);

        if (query.Offset.HasValue)
        {
            ordersQuery = ordersQuery.Skip(query.Offset.Value);
        }

        if (query.Limit.HasValue)
        {
            ordersQuery = ordersQuery.Take(query.Limit.Value);
        }

        List<BoltOrder> orders = await ordersQuery.ToListAsync(cancellationToken);

        var ordersList = orders.Select(o => new BoltOrderResponse(
            o.Id,
            o.OrderReference,
            o.DriverName,
            o.DriverPhone,
            o.PaymentMethod,
            o.OrderCreatedTime,
            o.OrderStatus,
            o.PickupAddress,
            o.DestinationAddress,
            o.RideDistance,
            o.RidePrice,
            o.NetEarnings,
            o.Tip,
            o.Commission,
            o.VehicleModel,
            o.VehicleLicensePlate,
            o.OrderFinishedTime)).ToList();

        return new BoltOrdersResponse(
            ordersList,
            totalOrdersCount,
            totalNetEarnings,
            totalCommissions,
            totalTips);
    }
}
