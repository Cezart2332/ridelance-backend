using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Vehicle;

/// <summary>Pasul 5 — clientul confirmă că a depus dosarul copie conformă la ARR.</summary>
public sealed record MarkCopyDossierSubmittedCommand(Guid UserId) : ICommand;

internal sealed class MarkCopyDossierSubmittedCommandHandler(
    IApplicationDbContext context,
    OnboardingStateService stateService)
    : ICommandHandler<MarkCopyDossierSubmittedCommand>
{
    public async Task<Result> Handle(MarkCopyDossierSubmittedCommand command, CancellationToken cancellationToken)
    {
        // Poarta RL-01: se scrie doar pe pasul activ. Prima verificare din handler —
        // altfel am valida conținutul unei cereri care oricum nu are voie să treacă.
        Result guard = await stateService.EnsureWritableAsync(
            command.UserId, OnboardingStepKey.Vehicle, cancellationToken);

        if (guard.IsFailure)
        {
            return Result.Failure(guard.Error);
        }

        VehicleCopyRequest? copy = await context.VehicleCopyRequests
            .Where(c => c.Vehicle.PfaRegistration.UserId == command.UserId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (copy is null)
        {
            return Result.Failure(VehicleShared.CopyRequestNotFound);
        }

        DateTime nowUtc = DateTime.UtcNow;
        copy.SubmittedAtUtc = nowUtc;
        if (copy.Status is VehicleCopyRequestStatus.Draft or VehicleCopyRequestStatus.DossierGenerated)
        {
            copy.Status = VehicleCopyRequestStatus.Submitted;
        }
        copy.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
