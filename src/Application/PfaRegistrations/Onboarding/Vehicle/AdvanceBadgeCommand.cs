using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Vehicle;

/// <summary>Pasul 5 — adminul avansează manual statusul unui set de ecusoane (plătit/emis).</summary>
public sealed record AdvanceBadgeCommand(
    Guid RegistrationId,
    PfaPlatformProvider Provider,
    VehicleBadgeStatus Status,
    Guid? BadgeDocumentId) : ICommand;

internal sealed class AdvanceBadgeCommandHandler(IApplicationDbContext context)
    : ICommandHandler<AdvanceBadgeCommand>
{
    public async Task<Result> Handle(AdvanceBadgeCommand command, CancellationToken cancellationToken)
    {
        VehicleBadge? badge = await context.VehicleBadges
            .Where(b => b.Vehicle.PfaRegistrationId == command.RegistrationId && b.Provider == command.Provider)
            .OrderByDescending(b => b.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (badge is null)
        {
            return Result.Failure(VehicleShared.VehicleNotFound);
        }

        badge.Status = command.Status;
        if (command.BadgeDocumentId is not null)
        {
            badge.BadgeDocumentId = command.BadgeDocumentId;
        }
        badge.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
