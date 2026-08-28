using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Checks;

/// <summary>Cronologia unei mașini, de la cel mai recent.</summary>
public sealed record GetVehicleTimelineQuery(Guid CarId) : IQuery<List<VehicleEventDto>>;

public sealed record VehicleEventDto(Guid Id, string Type, string Description, DateTime OccurredAtUtc);

internal sealed class GetVehicleTimelineQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetVehicleTimelineQuery, List<VehicleEventDto>>
{
    public async Task<Result<List<VehicleEventDto>>> Handle(
        GetVehicleTimelineQuery query,
        CancellationToken cancellationToken)
    {
        bool owns = await context.Cars
            .AsNoTracking()
            .AnyAsync(c => c.Id == query.CarId && c.PostedByUserId == userContext.UserId, cancellationToken);

        if (!owns)
        {
            return Result.Failure<List<VehicleEventDto>>(
                Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        List<VehicleEvent> events = await context.VehicleEvents
            .AsNoTracking()
            .Where(e => e.CarId == query.CarId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        return Result.Success(events
            .Select(e => new VehicleEventDto(e.Id, e.Type.ToString(), e.Description, e.OccurredAtUtc))
            .ToList());
    }
}
