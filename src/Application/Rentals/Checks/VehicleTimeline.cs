using Application.Abstractions.Data;
using Domain.Cars;

namespace Application.Rentals.Checks;

/// <summary>
/// Scrierea în cronologia unei mașini, dintr-un singur loc.
/// </summary>
/// <remarks>
/// Rândurile se adaugă în context, nu se salvează aici: evenimentul trebuie să apară în aceeași
/// tranzacție ca fapta pe care o descrie. Salvat separat, ar fi rămas în istoric și atunci când
/// acțiunea propriu-zisă a eșuat.
/// </remarks>
internal static class VehicleTimeline
{
    public static void Record(
        IApplicationDbContext context,
        Guid carId,
        VehicleEventType type,
        string description,
        Guid? rentalId = null,
        DateTime? occurredAtUtc = null)
    {
        context.VehicleEvents.Add(new VehicleEvent
        {
            Id = Guid.NewGuid(),
            CarId = carId,
            Type = type,
            Description = description,
            RentalId = rentalId,
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow,
        });
    }
}
